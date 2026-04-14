using KnockBox.Core.Extensions.Disposable;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.State.Shared;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace KnockBox.Services.State.Shared;

internal readonly record struct RegistrationKey(SessionToken SessionToken, Type ServiceType);

internal class CacheRegistration(IServiceScope scope, object service) : IDisposable
{
    public readonly object Service = service;

    public readonly Lock StateLock = new();
    public int ReferenceCount = 0;

    public void Dispose()
    {
        scope.Dispose();
    }
}

public class SessionServiceProvider(
    IServiceProvider serviceProvider,
    IMemoryCache cache,
    ILogger<SessionServiceProvider> logger)
    : ISessionServiceProvider, IDisposable
{
    public ValueResult<ServiceRegistration<TService>> GetService<TService>(SessionToken sessionToken)
    {
        try
        {
            var key = new RegistrationKey(sessionToken, typeof(TService));

            // 1. Get existing or create a new Lazy wrapper to prevent double-DI resolution
            var lazyRegistration = cache.GetOrCreate(key, entry =>
            {
                // Initial creation state: ensure it doesn't expire while we set it up
                entry.Priority = CacheItemPriority.NeverRemove;
                entry.RegisterPostEvictionCallback(EvictionCallback);

                return new Lazy<CacheRegistration>(() => CreateRegistration(key));
            });

            // 2. Resolve the actual instance
            var registration = lazyRegistration!.Value;

            // 3. Thread-safe increment and state transition
            lock (registration.StateLock)
            {
                registration.ReferenceCount++;

                // If it just transitioned from 0 -> 1 (or was just created), lock it in the cache
                if (registration.ReferenceCount == 1)
                {
                    var options = new MemoryCacheEntryOptions()
                        .SetPriority(CacheItemPriority.NeverRemove)
                        .RegisterPostEvictionCallback(EvictionCallback);

                    cache.Set(key, lazyRegistration, options);
                }
            }

            // 4. Create the cleanup token
            var lifecycleToken = new DisposableAction(() =>
            {
                lock (registration.StateLock)
                {
                    registration.ReferenceCount--;

                    // If no one is using it anymore, start the 1-minute countdown.
                    // We use an ExpirationToken (ChangeToken) to make the eviction proactive,
                    // otherwise IMemoryCache only evicts during the next cache operation.
                    if (registration.ReferenceCount <= 0)
                    {
                        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                        var options = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(1))
                            .AddExpirationToken(new CancellationChangeToken(cts.Token))
                            .RegisterPostEvictionCallback(EvictionCallback, state: cts);

                        cache.Set(key, lazyRegistration, options);
                    }
                }
            });

            return new ServiceRegistration<TService>(
                sessionToken,
                (TService)registration.Service,
                lifecycleToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve session-scoped service.");
            return new ResultError("Unable to get service.");
        }
    }

    private CacheRegistration CreateRegistration(RegistrationKey key)
    {
        var scope = serviceProvider.CreateScope();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService(key.ServiceType);
            return new CacheRegistration(scope, service);
        }
        catch
        {
            scope.Dispose(); // Clean up if DI resolution fails
            throw;
        }
    }

    private void EvictionCallback(object key, object? value, EvictionReason reason, object? state)
    {
        // Always dispose the state to avoid leaking resources.
        if (state is IDisposable disposable)
        {
            disposable.Dispose();
        }

        var regKey = (RegistrationKey)key;
        logger.LogDebug("Session cache eviction: {Type} for {Token}. Reason: {Reason}", regKey.ServiceType.Name, regKey.SessionToken.Token, reason);

        // We dispose the service if it expired (absolute time), token expired (CTS timer), 
        // was manually removed, or evicted due to capacity.
        // We do NOT dispose if it was Replaced (e.g., a player reconnected and "locked" the entry).
        if (reason == EvictionReason.Expired || 
            reason == EvictionReason.TokenExpired || 
            reason == EvictionReason.Removed || 
            reason == EvictionReason.Capacity)
        {
            if (value is Lazy<CacheRegistration> lazyReg && lazyReg.IsValueCreated)
            {
                lazyReg.Value.Dispose();
                logger.LogDebug(
                    "Session service {Type} for {Token} expired and was disposed. (Reason: {Reason})",
                    regKey.ServiceType.Name, 
                    regKey.SessionToken.Token,
                    reason);
            }
        }
    }

    public void Dispose()
    {
        // IMemoryCache doesn't provide a way to clear everything natively without disposing the cache itself, 
        // but relying on DI to dispose the IMemoryCache at app shutdown will clean this up.
    }
}
