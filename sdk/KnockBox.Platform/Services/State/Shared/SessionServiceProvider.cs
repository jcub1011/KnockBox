using KnockBox.Core.Extensions.Disposable;
using KnockBox.Core.Extensions.Returns;
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

public sealed class SessionServiceProvider(
    IServiceProvider serviceProvider,
    ILogger<SessionServiceProvider> logger)
    : ISessionServiceProvider, IDisposable
{
    private int _disposed = 0;
    private readonly ConcurrentDictionary<RegistrationKey, Lazy<CacheRegistration>> _services = [];
    internal TimeSpan EvictionDelay { get; set; } = TimeSpan.FromMinutes(1);

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
                var kvp = new KeyValuePair<RegistrationKey, Lazy<CacheRegistration>>(key, lazyRegistration);
                ((ICollection<KeyValuePair<RegistrationKey, Lazy<CacheRegistration>>>)_services).Remove(kvp);

                logger.LogError(ex, "Failed to resolve session-scoped service.");
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
            await Task.Delay(EvictionDelay, token);

            bool shouldEvict = false;

            lock (registrationToEvict.StateLock)
            {
                if (token.IsCancellationRequested || registrationToEvict.ReferenceCount > 0) return;

                registrationToEvict.IsEvicted = true;
                shouldEvict = true;

                // Atomically remove the exact instance from the dictionary while still under the state lock
                var entry = new KeyValuePair<RegistrationKey, Lazy<CacheRegistration>>(key, lazyRegistration);
                ((ICollection<KeyValuePair<RegistrationKey, Lazy<CacheRegistration>>>)_services).Remove(entry);
            }

            if (shouldEvict)
            {
                registrationToEvict.Dispose();
                logger.LogDebug("Session service {Type} for {Token} expired and was disposed.", key.ServiceType.Name, key.SessionToken.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling eviction for session service {Type} with token {Token}.", key.ServiceType.Name, key.SessionToken.Token);
        }
    }

    private CacheRegistration CreateRegistration(RegistrationKey key)
    {
        // Prevent instantiation if disposal has begun
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(SessionServiceProvider));

        var scope = serviceProvider.CreateScope();
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
