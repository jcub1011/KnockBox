using KnockBox.Core.Services.State.Shared;
using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Shared
{
    /// <summary>
    /// Singleton background service that drives a fixed-rate tick loop (20 ticks/second).
    /// Consumers register callbacks via <see cref="ITickService.RegisterTickCallback"/> and
    /// are automatically unsubscribed when the returned <see cref="IDisposable"/> is disposed.
    /// </summary>
    public sealed class TickService(ILogger<TickService> logger) : BackgroundService, ITickService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50); // 20 ticks/sec

        private readonly Lock _lock = new();
        private readonly Dictionary<int, HashSet<Action>> _tickMap = [];

        public TimeSpan TickInterval => Interval;

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
            logger.LogInformation("TickService started. Interval: {interval}ms.", Interval.TotalMilliseconds);

            using var timer = new PeriodicTimer(Interval);
            long tickCount = 0;

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    tickCount++;

                    List<Action> callbacks = [];
                    lock (_lock)
                    {
                        foreach (var kvp in _tickMap)
                        {
                            if (tickCount % kvp.Key == 0)
                                callbacks.AddRange(kvp.Value);
                        }
                    }

                    foreach (var callback in callbacks)
                    {
                        try
                        {
                            callback.Invoke();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error in tick callback.");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }

            logger.LogInformation("TickService stopped.");
        }
    }
}
