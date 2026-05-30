using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class PlayingPhase : ComponentBase
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<PlayingPhase> Logger { get; set; } = default!;

        [Parameter] public LinkedListGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        // Submitter input. The DOM owns the value (no `value="@_word"` binding) so
        // parent re-renders mid-keystroke don't clobber in-flight text; `_inputKey`
        // is bumped after a successful submit to force a clean input element.
        private string _word = string.Empty;
        private int _inputKey;

        // Auditor reject flow.
        private bool _rejecting;
        private string _reason = string.Empty;
        private int _reasonKey;

        protected void OnWordInput(ChangeEventArgs e)
            => _word = e.Value?.ToString() ?? string.Empty;

        protected void SubmitPair()
        {
            if (UserService.CurrentUser is null || string.IsNullOrWhiteSpace(_word)) return;

            var result = GameEngine.SubmitPair(UserService.CurrentUser, GameState, _word);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogInformation("Submission rejected: {Error}", error.InternalMessage);
                _ = OnError.InvokeAsync(error.PublicMessage);
            }
            else
            {
                _word = string.Empty;
                _inputKey++;
            }
        }

        protected void Approve()
        {
            if (UserService.CurrentUser is null) return;

            var result = GameEngine.Approve(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogInformation("Approve failed: {Error}", error.InternalMessage);
                _ = OnError.InvokeAsync(error.PublicMessage);
            }
            else
            {
                ResetRejectForm();
            }
        }

        protected void BeginReject() => _rejecting = true;

        protected void CancelReject() => ResetRejectForm();

        protected void OnReasonInput(ChangeEventArgs e)
            => _reason = e.Value?.ToString() ?? string.Empty;

        protected void ConfirmReject()
        {
            if (UserService.CurrentUser is null || string.IsNullOrWhiteSpace(_reason)) return;

            var result = GameEngine.Reject(UserService.CurrentUser, GameState, _reason);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogInformation("Reject failed: {Error}", error.InternalMessage);
                _ = OnError.InvokeAsync(error.PublicMessage);
            }
            else
            {
                ResetRejectForm();
            }
        }

        private void ResetRejectForm()
        {
            _rejecting = false;
            _reason = string.Empty;
            _reasonKey++;
        }

        protected string DisplayNameOf(string playerId)
            => GameState.GamePlayers.TryGetValue(playerId, out var ps) ? ps.DisplayName : "Someone";

        /// <summary>Formats banked thinking time as <c>m:ss</c> (or <c>h:mm:ss</c>).</summary>
        protected static string FormatElapsed(TimeSpan elapsed)
            => elapsed >= TimeSpan.FromHours(1)
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
    }
}
