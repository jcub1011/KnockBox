namespace KnockBox.Services.Logic.Admin
{
    internal sealed class AdminMetricsService : IAdminMetricsService
    {
        private readonly Lock _lock = new();
        private IReadOnlyList<string> _boundAddresses = [];
        private int _activeCircuits;

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
