namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Read-only surface used by the admin dashboard to show lightweight
    /// system telemetry: how long the host has been up, which URLs it
    /// bound to, and how many Blazor circuits are currently connected
    /// (a reasonable stand-in for "active users").
    /// </summary>
    public interface IAdminMetricsService
    {
        /// <summary>Moment the host finished starting.</summary>
        DateTime StartedAtUtc { get; }

        /// <summary>Uptime since <see cref="StartedAtUtc"/>.</summary>
        TimeSpan Uptime { get; }

        /// <summary>
        /// Addresses Kestrel reported bound when the host started.
        /// Populated by <c>Program.LogBoundAddresses</c>.
        /// </summary>
        IReadOnlyList<string> BoundAddresses { get; }

        /// <summary>Number of Blazor Server circuits currently open.</summary>
        int ActiveCircuitCount { get; }

        /// <summary>
        /// Sets the bound addresses once they are known (called from
        /// <see cref="IHostApplicationLifetime.ApplicationStarted"/>).
        /// </summary>
        void SetBoundAddresses(IEnumerable<string> addresses);

        /// <summary>Increments the active-circuit counter.</summary>
        void OnCircuitOpened();

        /// <summary>Decrements the active-circuit counter, clamped at zero.</summary>
        void OnCircuitClosed();
    }
}
