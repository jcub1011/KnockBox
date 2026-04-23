using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Shared;

internal readonly record struct RegistrationKey(SessionToken SessionToken, Type ServiceType);

internal class CacheRegistration(IServiceScope scope, object service) : IDisposable
{
    public readonly object Service = service;
    public readonly Lock StateLock = new();

    public int ReferenceCount = 0;
    public CancellationTokenSource? EvictionCts;

    public bool IsEvicted = false;

    public void Dispose()
    {
        lock (StateLock)
        {
            EvictionCts?.Cancel();
            EvictionCts?.Dispose();
            scope.Dispose();
        }
    }
}

public sealed class SessionServiceProvider : ISessionServiceProvider, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionServiceProvider> _logger;
    private readonly TimeProvider _timeProvider;

    private int _disposed = 0;
    private readonly ConcurrentDictionary<RegistrationKey, Lazy<CacheRegistration>> _services = [];
    internal TimeSpan EvictionDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Production constructor. When no <see cref="TimeProvider"/> is registered
    /// in DI, the container picks this shorter ctor (longest-viable constructor
    /// selection); it delegates to the full overload with
    /// <see cref="TimeProvider.System"/>. Register a <c>TimeProvider</c> at the
    /// container level if you want DI to pick the longer ctor instead. Tests
    /// construct the longer overload directly with a <c>FakeTimeProvider</c>.
    /// </summary>
    public SessionServiceProvider(
        IServiceProvider serviceProvider,
        ILogger<SessionServiceProvider> logger)
        : this(serviceProvider, logger, TimeProvider.System) { }

    /// <summary>
    /// Test-friendly constructor that accepts an explicit <see cref="TimeProvider"/> so
    /// eviction delay can be driven by a <c>FakeTimeProvider</c> without real wall-clock waits.
    /// </summary>
    public SessionServiceProvider(
        IServiceProvider serviceProvider,
        ILogger<SessionServiceProvider> logger,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public ValueResult<ServiceRegistration<TService>> GetService<TService>(SessionToken sessionToken)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return ValueResult<ServiceRegistration<TService>>.FromError("Unable to get service.", $"{nameof(SessionServiceProvider)} is disposed.");

        var key = new RegistrationKey(sessionToken, typeof(TService));

        while (true)
        {
            var lazyRegistration = _services.GetOrAdd(key, _ => new Lazy<CacheRegistration>(() => CreateRegistration(key)));

            CacheRegistration registration;
            try
            {
                registration = lazyRegistration.Value;
            }
            catch (Exception ex)
            {
                _services.TryRemove(new KeyValuePair<RegistrationKey, Lazy<CacheRegistration>>(key, lazyRegistration));

                _logger.LogError(ex, "Failed to resolve session-scoped service.");
                return new ResultError("Unable to get service.");
            }

            lock (registration.StateLock)
            {
                if (registration.IsEvicted) continue;

                registration.ReferenceCount++;

                if (registration.ReferenceCount == 1 && registration.EvictionCts is not null)
                {
                    registration.EvictionCts.Cancel();
                    registration.EvictionCts.Dispose();
                    registration.EvictionCts = null;
                }

                var lifecycleToken = new DisposableAction(() =>
                {
                    lock (registration.StateLock)
                    {
                        registration.ReferenceCount--;

                        if (registration.ReferenceCount <= 0 && registration.EvictionCts is null)
                        {
                            registration.EvictionCts = new CancellationTokenSource();
                            // Pass both the registration and the exact lazy wrapper for safe removal
                            _ = StartEvictionTimer(key, registration, lazyRegistration, registration.EvictionCts.Token);
                        }
                    }
                });

                return new ServiceRegistration<TService>(sessionToken, (TService)registration.Service, lifecycleToken);
            }
        }
    }

    private async Task StartEvictionTimer(RegistrationKey key, CacheRegistration registrationToEvict, Lazy<CacheRegistration> lazyRegistration, CancellationToken token)
    {
        try
        {
            // TimeProvider-aware delay lets tests drive eviction via FakeTimeProvider
            // without real wall-clock waits. Defaults to TimeProvider.System in prod.
            await Task.Delay(EvictionDelay, _timeProvider, token);

            // Provider already disposing — leave eviction to Dispose() to avoid racing it.
            if (Volatile.Read(ref _disposed) == 1) return;

            bool shouldEvict = false;

            lock (registrationToEvict.StateLock)
            {
                if (token.IsCancellationRequested || registrationToEvict.ReferenceCount > 0) return;

                registrationToEvict.IsEvicted = true;
                shouldEvict = true;

                // Atomically remove the exact instance from the dictionary while still under the state lock.
                _services.TryRemove(new KeyValuePair<RegistrationKey, Lazy<CacheRegistration>>(key, lazyRegistration));
            }

            if (shouldEvict)
            {
                registrationToEvict.Dispose();
                _logger.LogDebug("Session service {Type} for {Token} expired and was disposed.", key.ServiceType.Name, key.SessionToken.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling eviction for session service {Type} with token {Token}.", key.ServiceType.Name, key.SessionToken.Token);
        }
    }

    private CacheRegistration CreateRegistration(RegistrationKey key)
    {
        // Prevent instantiation if disposal has begun
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(SessionServiceProvider));

        var scope = _serviceProvider.CreateScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService(key.ServiceType);
            return new CacheRegistration(scope, service);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        // Take a snapshot to safely dispose items without concurrent modification issues
        var items = _services.ToArray();
        _services.Clear();

        foreach (var kvp in items)
        {
            if (kvp.Value.IsValueCreated)
            {
                kvp.Value.Value.Dispose();
            }
        }
    }
}
