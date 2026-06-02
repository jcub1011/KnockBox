using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain.Pages
{
    public partial class AlphaChainLobby : LobbyPageBase<AlphaChainGameState>
    {
        [Inject] protected AlphaChainGameEngine GameEngine { get; set; } = default!;

        /// <summary>
        /// Host-only tick that drives the FSM clock. A no-op in M1 (the shot clock has
        /// no consequence yet) but wired now so timed transitions in M2 work without
        /// touching the lobby again.
        /// </summary>
        protected override bool TryGetHostTick(out Action action, out int tickInterval)
        {
            action = () =>
            {
                if (GameState?.Context is not null)
                    GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
            };
            tickInterval = TickService.TicksPerSecond;
            return true;
        }

        /// <summary>
        /// Two lobby buttons share this handler: <c>StartGame(false)</c> runs the host as
        /// the shared display; <c>StartGame(true)</c> deals the host in as a player. The
        /// choice is written through <c>UpdateSettings</c> (which reflects <c>HostPlays</c>
        /// into <c>HostIsParticipant</c>) immediately before the engine snapshots participants.
        /// </summary>
        protected async Task StartGame(bool hostPlays)
        {
            if (UserService.CurrentUser is null) return;

            if (GameState.UpdateSettings(s => s with { HostPlays = hostPlays }).TryGetFailure(out var settingsError))
            {
                Logger.LogError("Failed to set host-plays before start: {Error}", settingsError.PublicMessage);
                return;
            }

            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start Alpha Chain: {Error}", error.PublicMessage);
        }
    }
}
