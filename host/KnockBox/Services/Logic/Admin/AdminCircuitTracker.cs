using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Blazor Server circuit handler that keeps <see cref="IAdminMetricsService"/>
    /// up to date with the current connected-circuit count. Registered as a
    /// scoped service -- the framework instantiates one per circuit and calls
    /// <see cref="CircuitOpenedAsync"/>/<see cref="CircuitClosedAsync"/> on the
    /// matching pair, so per-instance handler identity is naturally balanced.
    /// </summary>
    internal sealed class AdminCircuitTracker : CircuitHandler
    {
        private readonly IAdminMetricsService _metrics;

        public AdminCircuitTracker(IAdminMetricsService metrics)
        {
            _metrics = metrics;
        }

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _metrics.OnCircuitOpened();
            return Task.CompletedTask;
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _metrics.OnCircuitClosed();
            return Task.CompletedTask;
        }
    }
}
