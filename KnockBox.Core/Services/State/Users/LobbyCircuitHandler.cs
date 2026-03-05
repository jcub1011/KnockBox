using KnockBox.Services.State.Games.Shared;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KnockBox.Services.State.Users
{
    public sealed class LobbyCircuitHandler(
        IGameSessionService gameSessionService,
        ILogger<LobbyCircuitHandler> logger) : CircuitHandler
    {
        private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromSeconds(30);
        private CancellationTokenSource? _disconnectCts;

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            StartDisconnectTimer();
            return Task.CompletedTask;
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            CancelDisconnectTimer();
            return Task.CompletedTask;
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            CancelDisconnectTimer();
            SafeLeaveSession();
            return Task.CompletedTask;
        }

        private void StartDisconnectTimer()
        {
            CancelDisconnectTimer();
            _disconnectCts = new();

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_disconnectCts is not null)
                    {
                        await Task.Delay(DisconnectGracePeriod, _disconnectCts.Token);
                    }

                    SafeLeaveSession();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling lobby cleanup after disconnect.");
                }
            });
        }

        private void CancelDisconnectTimer()
        {
            try
            {
                _disconnectCts?.Cancel();
                _disconnectCts?.Dispose();
            }
            finally
            {
                _disconnectCts = null;
            }
        }

        private void SafeLeaveSession()
        {
            try
            {
                gameSessionService.LeaveCurrentSession(navigateHome: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error leaving lobby after circuit disconnect.");
            }
        }
    }
}