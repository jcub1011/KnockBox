using System.Collections.Concurrent;

namespace KnockBox.Services.State
{
    /// <summary>
    /// Singleton implementation of <see cref="IIDBackedServiceProvider"/>.
    /// <para>
    /// Services are stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
    /// <c>(id, Type)</c> pairs. Multiple Blazor circuits that share the same user-session id
    /// share the same service instances. Services are disposed after <paramref name="disposalDelay"/>
    /// (default 5 minutes) once the last active circuit for an id closes, unless a new request
    /// arrives first.
    /// </para>
    /// </summary>
    public sealed class IDBackedServiceProvider(
        IServiceProvider serviceProvider,
        ILogger<IDBackedServiceProvider> logger,
        TimeSpan? disposalDelay = null) : IIDBackedServiceProvider
    {
        private readonly TimeSpan _disposalDelay = disposalDelay ?? TimeSpan.FromMinutes(5);

        // Key is (userId, serviceType) → cached service instance
        private readonly ConcurrentDictionary<(string Id, Type Type), object> _services = new();

        // Tracks which circuit ids are currently active for each user id.
        // Values are used as a concurrent set via ConcurrentDictionary<circuitId, bool>.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _activeCircuits = new();

        // Maps user id → active disposal CancellationTokenSource
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _disposalTimers = new();

        /// <inheritdoc/>
        public T? GetService<T>(string id) where T : class
        {
            // A new request means this id is still alive — cancel any pending disposal.
            CancelDisposalTimer(id);

            var service = _services.GetOrAdd((id, typeof(T)), _ =>
            {
                var resolved = serviceProvider.GetService<T>();
                return resolved is not null ? (object)resolved : NullServiceSentinel.Instance;
            });

            return service is NullServiceSentinel ? null : (T)service;
        }

        /// <inheritdoc/>
        public void NotifyCircuitActive(string id, string circuitId)
        {
            _activeCircuits
                .GetOrAdd(id, _ => new ConcurrentDictionary<string, bool>())
                .TryAdd(circuitId, true);

            CancelDisposalTimer(id);
        }

        /// <inheritdoc/>
        public void NotifyCircuitClosed(string id, string circuitId)
        {
            if (!_activeCircuits.TryGetValue(id, out var circuits))
            {
                // No circuits were tracked for this id — start cleanup in case services exist.
                StartDisposalTimer(id);
                return;
            }

            circuits.TryRemove(circuitId, out _);

            if (circuits.IsEmpty)
            {
                StartDisposalTimer(id);
            }
        }

        private void CancelDisposalTimer(string id)
        {
            if (_disposalTimers.TryRemove(id, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                finally
                {
                    cts.Dispose();
                }
            }
        }

        private void StartDisposalTimer(string id)
        {
            var cts = new CancellationTokenSource();

            if (!_disposalTimers.TryAdd(id, cts))
            {
                // Another timer is already running for this id.
                cts.Dispose();
                return;
            }

            // The CTS is always disposed in the finally block below — either after
            // DisposeServicesForId completes normally, or after it is cancelled via
            // CancelDisposalTimer (which removes the CTS from the dictionary and cancels it
            // before the timer fires; the subsequent Dispose() here is safe and idempotent).
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_disposalDelay, cts.Token);
                    DisposeServicesForId(id);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during deferred disposal of services for id [{Id}].", id);
                }
                finally
                {
                    cts.Dispose();
                }
            });
        }

        private void DisposeServicesForId(string id)
        {
            // Remove the dictionary entry; the CTS itself is disposed by the Task.Run finally block.
            _disposalTimers.TryRemove(id, out _);
            _activeCircuits.TryRemove(id, out _);

            foreach (var key in _services.Keys.Where(k => k.Id == id).ToList())
            {
                if (!_services.TryRemove(key, out var service))
                    continue;

                if (service is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Error disposing service [{Type}] for id [{Id}].",
                            key.Type.Name,
                            id);
                    }
                }
            }
        }

        // Sentinel used instead of storing null in the ConcurrentDictionary
        // (ConcurrentDictionary does not support null values).
        private sealed class NullServiceSentinel
        {
            public static readonly NullServiceSentinel Instance = new();
            private NullServiceSentinel() { }
        }
    }
}
