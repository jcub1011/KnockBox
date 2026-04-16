using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace KnockBox.Services.Logic.Admin
{
    internal sealed class AdminMetricsService : BackgroundService, IAdminMetricsService
    {
        private readonly Lock _lock = new();
        private IReadOnlyList<string> _boundAddresses = [];
        private int _activeCircuits;

        private double _cpuUtilization;
        private long _memoryUsageBytes;

        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        public TimeSpan Uptime => DateTime.UtcNow - StartedAtUtc;

        public IReadOnlyList<string> BoundAddresses
        {
            get
            {
                lock (_lock) return _boundAddresses;
            }
        }

        public int ActiveCircuitCount => Volatile.Read(ref _activeCircuits);

        public double CpuUtilization => Volatile.Read(ref _cpuUtilization);

        public long MemoryUsageBytes => Volatile.Read(ref _memoryUsageBytes);

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            using var metricsTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                var process = Process.GetCurrentProcess();
                var processorCount = Environment.ProcessorCount;
                var lastTime = DateTime.UtcNow;
                var lastTotalProcessorTime = process.TotalProcessorTime;

                while (await metricsTimer.WaitForNextTickAsync(ct))
                {
                    // Update Memory
                    process.Refresh();
                    Volatile.Write(ref _memoryUsageBytes, process.PrivateMemorySize64);

                    // Update CPU
                    var currentTime = DateTime.UtcNow;
                    var currentTotalProcessorTime = process.TotalProcessorTime;

                    var cpuUsedMs = (currentTotalProcessorTime - lastTotalProcessorTime).TotalMilliseconds;
                    var totalMsPassed = (currentTime - lastTime).TotalMilliseconds;
                    var cpuUsageTotal = cpuUsedMs / (processorCount * totalMsPassed);

                    Volatile.Write(ref _cpuUtilization, Math.Max(0, Math.Min(1, cpuUsageTotal)));

                    lastTime = currentTime;
                    lastTotalProcessorTime = currentTotalProcessorTime;
                }
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch { /* Ignore process errors in sampling loop */ }
        }

        public void SetBoundAddresses(IEnumerable<string> addresses)
        {
            lock (_lock)
            {
                _boundAddresses = addresses.ToArray();
            }
        }

        public void OnCircuitOpened()
        {
            Interlocked.Increment(ref _activeCircuits);
        }

        public void OnCircuitClosed()
        {
            // Clamp at 0 so bugs in open/close pairing can't produce negatives.
            int current, next;
            do
            {
                current = Volatile.Read(ref _activeCircuits);
                if (current <= 0) return;
                next = current - 1;
            } while (Interlocked.CompareExchange(ref _activeCircuits, next, current) != current);
        }
    }
}
