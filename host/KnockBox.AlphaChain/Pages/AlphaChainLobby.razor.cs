using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.PlayLog;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain.Pages
{
    public partial class AlphaChainLobby : LobbyPageBase<AlphaChainGameState>
    {
        /// <summary>Stable game id for the play log; must match the plugin's route identifier.</summary>
        private const string RouteIdentifier = "alpha-chain";

        [Inject] protected AlphaChainGameEngine GameEngine { get; set; } = default!;

        /// <summary>
        /// Records one play-log entry per user once the match reaches <see cref="AlphaChainGamePhase.GameOver"/>.
        /// Returns <c>null</c> while the game is still in progress so the base hook logs exactly the first
        /// terminal result.
        /// </summary>
        protected override GameLog? BuildEndOfGamePlayLog()
        {
            if (GameState.Phase != AlphaChainGamePhase.GameOver || GameState.Results is null)
                return null;

            return GameLog.Create(
                RouteIdentifier,
                AlphaChainPlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id));
        }

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
