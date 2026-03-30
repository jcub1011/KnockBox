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

        /// <summary>
        /// Runs the tick loop, invoking <paramref name="onTick"/> at fixed wall-clock intervals.
        /// <para>
        /// The <c>tickNumber</c> passed to <paramref name="onTick"/> represents a wall-clock time
        /// slot, <b>not</b> an invocation count. When the loop falls behind (e.g. a CPU stall or
        /// GC pause), it catches up by advancing <c>tickNumber</c> for every missed slot but caps
        /// actual callback invocations to <see cref="MAX_QUEUED_TICKS"/> per frame. Consumers may
        /// therefore observe gaps in tick numbers (e.g. 0, 1, 2, 5) — this is by design.
        /// </para>
        /// </summary>
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

                    // When the loop falls behind, we iterate over each missed tick slot.
                    // Actual callback invocations are capped to MAX_QUEUED_TICKS per frame
                    // to prevent a burst of work after a stall. The tick number always
                    // advances regardless, so consumers may see gaps — tick numbers represent
                    // wall-clock time slots, not invocation counts.
                    if (processedThisFrame++ < MAX_QUEUED_TICKS)
                        onTick.Invoke(tickNumber);

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
