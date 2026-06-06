using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword.Pages
{
    public partial class CluePhase : ComponentBase
    {
        [Inject] protected CodewordGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<CluePhase> Logger { get; set; } = default!;

        [Parameter] public CodewordGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private string _clueText = string.Empty;

        // Bumped after a successful submit to force Blazor to recreate the input
        // element, clearing the DOM-owned value. The DOM owns its value (no
        // `value="@_clueText"` binding) so that parent re-renders mid-keystroke
        // don't clobber in-flight input.
        private int _inputKey;

        protected void OnClueInput(ChangeEventArgs e)
        {
            _clueText = e.Value?.ToString() ?? string.Empty;

            // PERF: deliberate AbstractGameState.Execute bypass — see
            // host/KnockBox/Specs/knockbox-platform-architecture.md ("Documented
            // exception — per-keystroke pending-clue write"). Notifying on every
            // keystroke would re-render every subscriber and is exactly the storm
            // we're avoiding. The Tick reader and this writer race on a string
            // reference, which is atomic, so the worst case is the timeout grabs
            // the previous keystroke.
            var myId = UserService.CurrentUser?.Id;
            if (myId is { } id && GameState.GamePlayers.TryGetValue(id, out var player) && !player.HasSubmittedClue)
            {
                player.PendingClue = _clueText;
            }
        }

        protected void SubmitClue()
        {
            if (UserService.CurrentUser == null || string.IsNullOrWhiteSpace(_clueText)) return;

            var result = GameEngine.SubmitClue(UserService.CurrentUser, GameState, _clueText.Trim());
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to submit clue: {Error}", error);
                _ = OnError.InvokeAsync("Clue not accepted. Try a different word.");
            }
            else
            {
                _clueText = string.Empty;
                _inputKey++;
            }
        }
    }
}

