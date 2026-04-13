using KnockBox.Core.Services.State.Shared;
using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Returns;
using Microsoft.Extensions.ObjectPool;

namespace KnockBox.Services.State.Shared
{
    /// <summary>
    /// Singleton background service that drives a fixed-rate tick loop (20 ticks/second).
    /// Consumers register callbacks via <see cref="ITickService.RegisterTickCallback"/> and
    /// are automatically unsubscribed when the returned <see cref="IDisposable"/> is disposed.
    /// </summary>
    public sealed class TickService(ILogger<TickService> logger) : BackgroundService, ITickService
    {
        private const int TPS = 20;
        private static readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(1000 / TPS);
        private static readonly ObjectPool<List<Action>> CallbackPool =
            ObjectPool.Create<List<Action>>();

        private readonly Lock _lock = new();
        private readonly Dictionary<int, HashSet<Action>> _tickMap = [];

        public int TicksPerSecond => TPS;
        public TimeSpan TickInterval => _tickInterval;

        public ValueResult<IDisposable> RegisterTickCallback(Action tickCallback, int tickInterval = 1)
        {
            if (tickCallback is null)
                return ValueResult<IDisposable>.FromError("Tick callback must not be null.");

            if (tickInterval < 1)
                return ValueResult<IDisposable>.FromError("Tick interval must be at least 1.");

            bool isDuplicate = false;
            lock (_lock)
            {
                if (!_tickMap.TryGetValue(tickInterval, out var set))
                {
                    set = [];
                    _tickMap.Add(tickInterval, set);
                }

                isDuplicate = !set.Add(tickCallback);
            }

            if (isDuplicate) return ValueResult<IDisposable>.FromError("Callback is already registered.");
            else return new DisposableAction(() =>
            {
                bool regMissing = false;
                lock (_lock)
                {
                    if (_tickMap.TryGetValue(tickInterval, out var set))
                    {
                        regMissing = !set.Remove(tickCallback);
                        if (set.Count == 0) _tickMap.Remove(tickInterval);
                    }
                    else regMissing = true;
                }

                if (regMissing) logger.LogWarning("Tick callback was removed from tick map before registration token was disposed.");
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("TickService started. Interval: {interval}ms.", TickInterval.TotalMilliseconds);

            var fixedLoop = new FixedTickLoop(TickInterval);

            try
            {
                await fixedLoop.RunAsync((tick) =>
                {
                    var callbacks = CallbackPool.Get();
                    try
                    {
                        lock (_lock)
                        {
                            foreach (var kvp in _tickMap)
                            {
                                if (tick % kvp.Key == 0)
                                    callbacks.AddRange(kvp.Value);
                            }
                        }

                        foreach (var callback in callbacks)
                        {
                            try
                            {
                                callback();
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Error in tick callback.");
                            }
                        }
                    }
                    finally
                    {
                        callbacks.Clear();
                        CallbackPool.Return(callbacks);
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException) { } // Ignore cancellation errors
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling tick.");
            }

            logger.LogInformation("TickService stopped.");
        }
    }
}
