using System.Diagnostics;

namespace KnockBox.Core.Services.State.Shared
{
    /// <summary>
    /// Invokes a callback on intervals that corrects against time drift.
    /// </summary>
    /// <param name="tickInterval"></param>
    public class FixedTickLoop(TimeSpan tickInterval)
    {
        private const int MAX_QUEUED_TICKS = 3;
        private readonly long _tickIntervalTicks = (long)(tickInterval.TotalSeconds * Stopwatch.Frequency);
        private readonly Stopwatch _stopwatch = new();
        private long _nextTickAt;

        public async Task RunAsync(Action<long> onTick, CancellationToken ct)
        {
            _stopwatch.Start();
            _nextTickAt = _stopwatch.ElapsedTicks + _tickIntervalTicks;
            long tickNumber = 0;

            while (!ct.IsCancellationRequested)
            {
                long now = _stopwatch.ElapsedTicks;
                int processedThisFrame = 0;

                while (now >= _nextTickAt)
                {
                    _nextTickAt += _tickIntervalTicks;

                    // cap number of ticks processed in a single frame
                    if (processedThisFrame++ < MAX_QUEUED_TICKS) 
                        onTick.Invoke(tickNumber);

                    // always advance tick
                    tickNumber++;
                }

                long remaining = _nextTickAt - _stopwatch.ElapsedTicks;
                double remainingMs = (double)remaining / Stopwatch.Frequency * 1000.0;

                if (remainingMs > 2)
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingMs - 1), ct);
                else
                    await Task.Delay(TimeSpan.FromMilliseconds(1), ct);
            }
        }
    }
}
