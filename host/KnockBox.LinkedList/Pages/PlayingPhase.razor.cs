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
        }

        protected void Reject()
        {
            if (UserService.CurrentUser is null) return;

            var result = GameEngine.Reject(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogInformation("Reject failed: {Error}", error.InternalMessage);
                _ = OnError.InvokeAsync(error.PublicMessage);
            }
        }

        /// <summary>True when the current user is the lobby host — gates the
        /// "End round now" escape hatch.</summary>
        protected bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        protected void EndRound()
        {
            if (UserService.CurrentUser is null) return;

            var result = GameEngine.EndRound(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogInformation("End round failed: {Error}", error.InternalMessage);
                _ = OnError.InvokeAsync(error.PublicMessage);
            }
        }

        protected string DisplayNameOf(Guid playerId)
            => GameState.GamePlayers.TryGetValue(playerId, out var ps) ? ps.DisplayName : "Someone";

        // ── Groups (competitive) helpers (§8.2) ──────────────────────────────

        /// <summary>True when this match splits players into competing groups.</summary>
        protected bool IsGroups => GameState.Settings.PlayerStructure == PlayerStructure.Groups;

        /// <summary>The group the current user plays in, or <c>null</c> (Auditor / host
        /// spectator / not yet seated).</summary>
        protected ChainState? MyGroup =>
            UserService.CurrentUser is { } u ? GameState.TryGroupOf(u.Id) : null;

        /// <summary>The group whose submission the Auditor is currently judging.</summary>
        protected ChainState? AuditingGroup => GameState.AuditingGroup;

        /// <summary>Groups other than <paramref name="mine"/>, for the rival-progress strip.</summary>
        protected IReadOnlyList<ChainState> RivalsOf(ChainState? mine)
            => [.. GameState.Groups.Where(g => g.GroupId != mine?.GroupId)];

        /// <summary>Formats banked thinking time as <c>m:ss</c> (or <c>h:mm:ss</c>).</summary>
        protected static string FormatElapsed(TimeSpan elapsed)
            => elapsed >= TimeSpan.FromHours(1)
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
    }
}
