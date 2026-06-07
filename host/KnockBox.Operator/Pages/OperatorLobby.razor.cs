using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Operator.Pages
{
    public partial class OperatorLobby : LobbyPageBase<OperatorGameState>
    {
        /// <summary>Stable game id for the play log; must match the plugin's route identifier.</summary>
        private const string RouteIdentifier = "operator";

        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        // ── Error toast state ─────────────────────────────────────────────────
        private string? _errorMessage;
        private int _errorKey;

        /// <summary>
        /// Records one play-log entry per user once the match reaches
        /// <see cref="OperatorGamePhase.GameOver"/>. Returns <c>null</c> while the game is
        /// still in progress so the base hook logs exactly the first terminal result.
        /// </summary>
        protected override GameLog? BuildEndOfGamePlayLog()
        {
            if (GameState.Phase != OperatorGamePhase.GameOver)
                return null;

            return GameLog.Create(
                RouteIdentifier,
                OperatorPlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id));
        }

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

        // ── Error toast ───────────────────────────────────────────────────────

        // Mirror the CSS .op-error-toast animation duration in OperatorLobby.razor.css.
        private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(3);

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            int capturedKey = _errorKey;
            StateHasChanged();

            // Server-side dismissal because WebKit (iPhone Safari) tears the
            // circuit on `@onanimationend`.
            ScheduleClear(ErrorToastDuration, () =>
            {
                if (_errorKey == capturedKey) _errorMessage = null;
            });
        }
    }
}
