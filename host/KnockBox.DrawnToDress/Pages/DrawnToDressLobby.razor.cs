using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.PlayLog;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class DrawnToDressLobby : LobbyPageBase<DrawnToDressGameState>
    {
        /// <summary>Stable game id for the play log; must match the plugin's route identifier.</summary>
        private const string RouteIdentifier = "drawn-to-dress";

        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        private DtdAriaAnnouncer? _announcer;
        private GamePhase? _lastAnnouncedPhase;

        /// <summary>
        /// Records one play-log entry per user once the match reaches the terminal
        /// <see cref="GamePhase.Results"/> display phase (entered by
        /// <see cref="Services.Logic.Games.FSM.States.FinalResultsDisplayState"/>). Returns
        /// <c>null</c> while the game is still in progress so the base hook logs exactly the
        /// first terminal result.
        /// </summary>
        protected override GameLog? BuildEndOfGamePlayLog()
        {
            if (GameState.Phase != GamePhase.Results)
                return null;

            return GameLog.Create(
                RouteIdentifier,
                DrawnToDressPlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id));
        }

        /// <summary>
        /// Host-only tick that drives the FSM clock so timed phases advance even when the
        /// host's circuit is the only one connected.
        /// </summary>
        protected override bool TryGetHostTick(out Action action, out int tickInterval)
        {
            action = () =>
            {
                if (GameState?.Context is not null)
                    GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
            };
            tickInterval = TickService.TicksPerSecond; // once per second
            return true;
        }

        /// <summary>
        /// Re-renders on every state change and announces phase transitions to assistive
        /// technology via the ARIA live region.
        /// </summary>
        protected override async ValueTask OnStateChangedAsync()
        {
            await InvokeAsync(() =>
            {
                AnnouncePhaseChangeIfNeeded();
                StateHasChanged();
            });
        }

        private void AnnouncePhaseChangeIfNeeded()
        {
            if (GameState is null || _announcer is null) return;
            var currentPhase = GameState.Phase;
            if (_lastAnnouncedPhase != currentPhase)
            {
                _lastAnnouncedPhase = currentPhase;

                var phaseName = currentPhase switch
                {
                    GamePhase.ThemeSelection => "Theme Selection",
                    GamePhase.Drawing => "Drawing Round",
                    GamePhase.PoolReveal => "Pool Reveal",
                    GamePhase.OutfitBuilding => "Outfit Building",
                    GamePhase.OutfitCustomization => "Outfit Customization",
                    GamePhase.Voting => "Voting",
                    GamePhase.CoinFlip => "Coin Flip",
                    GamePhase.VotingRoundResults => "Voting Round Results",
                    GamePhase.Results => "Final Results",
                    _ => currentPhase.ToString()
                };
                _announcer.Announce($"Phase changed to {phaseName}");
            }
        }
    }
}
