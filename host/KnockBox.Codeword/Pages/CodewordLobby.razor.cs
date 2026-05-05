using KnockBox.Core.Components.Shared;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
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
            if (GameState?.Config is not { } config) return 1;
            return GameState.Phase switch
            {
                CodewordGamePhase.Setup => config.SetupPhaseTimeoutMs,
                CodewordGamePhase.CluePhase => config.CluePhaseTimeoutMs,
                CodewordGamePhase.Discussion => config.DiscussionPhaseTimeoutMs,
                CodewordGamePhase.Voting => config.VotePhaseTimeoutMs,
                CodewordGamePhase.Reveal => config.RevealPhaseTimeoutMs,
                CodewordGamePhase.ContinueOrEndRound => config.ContinueOrEndRoundPhaseTimeoutMs,
                _ => 1
            };
        }

        // Mirror the CSS .ctc-error-toast animation duration in CodewordLobby.razor.css.
        private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(3);

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            int capturedKey = _errorKey;
            StateHasChanged();

            // WebKit rejects setAttribute('@onanimationend', ...) and tears the
            // circuit on iPhone Safari, so dismissal is driven by a server-side
            // timer instead of the DOM animationend event. Guard against rapid
            // successive errors via the key.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ErrorToastDuration, ComponentDetached);
                    if (_errorKey == capturedKey)
                    {
                        _errorMessage = null;
                        await InvokeAsync(StateHasChanged);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
    }
}
