using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KnockBox.Services.State
{
    /// <summary>
    /// Scoped <see cref="CircuitHandler"/> that keeps <see cref="IIDBackedServiceProvider"/> informed
    /// of circuit lifecycle events. When a connection comes up the circuit is registered as active for
    /// the current user's id, and when the circuit closes it is removed — triggering the 5-minute
    /// disposal grace period if no other circuit shares the same id.
    /// </summary>
    public sealed class IDBackedCircuitHandler(
        IUserService userService,
        IIDBackedServiceProvider idBackedServiceProvider,
        ILogger<IDBackedCircuitHandler> logger) : CircuitHandler
    {
        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (userService.CurrentUser?.Id is string userId)
            {
                idBackedServiceProvider.NotifyCircuitActive(userId, circuit.Id);
            }
            else
            {
                logger.LogDebug(
                    "Circuit [{CircuitId}] connection came up but user service was not yet initialized; skipping ID-backed service registration.",
                    circuit.Id);
            }

            return Task.CompletedTask;
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (userService.CurrentUser?.Id is string userId)
            {
                idBackedServiceProvider.NotifyCircuitClosed(userId, circuit.Id);
            }
            else
            {
                logger.LogDebug(
                    "Circuit [{CircuitId}] closed but user service was not initialized; skipping ID-backed service cleanup.",
                    circuit.Id);
            }

            return Task.CompletedTask;
        }
    }
}
