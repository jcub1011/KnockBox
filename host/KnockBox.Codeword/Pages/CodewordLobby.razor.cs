using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.PlayLog;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword.Pages
{
    public partial class CodewordLobby : LobbyPageBase<CodewordGameState>
    {
        [Inject] protected CodewordGameEngine GameEngine { get; set; } = default!;

        private string? _errorMessage;
        private int _errorKey;

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

        protected int GetPhaseTotalMs()
        {
            if (GameState?.Settings is not { } settings) return 1;
            return GameState.Phase switch
            {
                CodewordGamePhase.Setup => settings.SetupPhaseTimeoutMs,
                CodewordGamePhase.CluePhase => settings.CluePhaseTimeoutMs,
                CodewordGamePhase.Discussion => settings.DiscussionPhaseTimeoutMs,
                CodewordGamePhase.Voting => settings.VotePhaseTimeoutMs,
                CodewordGamePhase.Reveal => settings.RevealPhaseTimeoutMs,
                CodewordGamePhase.ContinueOrEndRound => settings.ContinueOrEndRoundPhaseTimeoutMs,
                _ => 1
            };
        }

        /// <summary>
        /// Records a single match-level play-log entry when the whole match is over.
        /// Codeword runs multiple games per match, so <see cref="CodewordGamePhase.GameOver"/>
        /// is reached at the end of every game; the entry must only be written once the
        /// final game has finished (<c>CurrentGameNumber &gt;= Settings.TotalGames</c>).
        /// </summary>
        protected override GameLog? BuildEndOfGamePlayLog()
        {
            if (GameState.Phase != CodewordGamePhase.GameOver
                || GameState.CurrentGameNumber < GameState.Settings.TotalGames)
            {
                return null;
            }

            var metadata = CodewordPlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id);
            return GameLog.Create("codeword", metadata);
        }

        // Mirror the CSS .ctc-error-toast animation duration in CodewordLobby.razor.css.
        private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(3);

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            int capturedKey = _errorKey;
            StateHasChanged();

            // Server-side dismissal because WebKit (iPhone Safari) tears the
            // circuit on `@onanimationend`. Guard against rapid successive
            // errors via the captured key.
            ScheduleClear(ErrorToastDuration, () =>
            {
                if (_errorKey == capturedKey) _errorMessage = null;
            });
        }
    }
}
