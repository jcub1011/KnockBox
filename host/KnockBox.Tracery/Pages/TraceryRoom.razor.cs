using KnockBox.Core.Components.Shared;
using KnockBox.Tracery.Components;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Tracery.Pages
{
    public partial class TraceryRoom : LobbyPageBase<TraceryGameState>
    {
        [Inject] protected TraceryGameEngine GameEngine { get; set; } = default!;

        private string? _toastMessage;
        private TraceryToast.ToastTone _toastTone = TraceryToast.ToastTone.Danger;
        private bool _invalidTrace;
        private CancellationTokenSource? _toastCts;
        private CancellationTokenSource? _shakeCts;

        /// <summary>
        /// True when the current user is the host and is sitting out as the shared display
        /// (not a participant). Drives the host-vs-player view split during play.
        /// </summary>
        protected bool IsHostObserver =>
            GameState is not null
            && !GameState.HostIsParticipant
            && UserService.CurrentUser?.Id == GameState.Host.Id;

        /// <summary>The current user's per-round player state, or null for an observer/stranger.</summary>
        private TraceryPlayerState? CurrentPlayerState =>
            UserService.CurrentUser is { Id: var id } && GameState.TryGetPlayerState(id, out var ps) ? ps : null;

        /// <summary>This player's banked words, longest first then alphabetical, for the list.</summary>
        private IEnumerable<string> BankedWords()
            => CurrentPlayerState is { } ps
                ? ps.BankedWords.Keys.OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal)
                : [];

        /// <summary>The frozen participant roster ordered by cumulative score, for standings.</summary>
        private IEnumerable<(string DisplayName, int Score)> Standings()
            => GameState.Participants
                .Select(entry => (
                    entry.DisplayName,
                    Score: GameState.PlayerStates.TryGetValue(entry.User.Id, out var ps) ? ps.CumulativeScore : 0))
                .OrderByDescending(x => x.Score);

        /// <summary>
        /// Submit handler shared by drag and tap: routes the captured path through the engine
        /// and surfaces accept/reject feedback. A duplicate of an already-banked word comes back
        /// as silent success (the engine no-ops it), so no toast fires for it.
        /// </summary>
        private async Task HandlePathSubmitted(IReadOnlyList<int> path)
        {
            if (GameState is null || UserService.CurrentUser is null || IsHostObserver) return;

            var preBankCount = CurrentPlayerState?.BankedWords.Count ?? 0;
            var result = GameEngine.SubmitTrace(GameState, UserService.CurrentUser, path);

            if (result.IsSuccess)
            {
                // Only celebrate when the bank actually grew — a re-trace of a known word is a no-op.
                if ((CurrentPlayerState?.BankedWords.Count ?? 0) > preBankCount)
                    await ShowToast("Nice!", TraceryToast.ToastTone.Success);
            }
            else if (result.TryGetFailure(out var failure))
            {
                await Task.WhenAll(
                    ShowToast(failure.PublicMessage, TraceryToast.ToastTone.Danger),
                    TriggerInvalidShake());
            }
            StateHasChanged();
        }

        private async Task ShowToast(string message, TraceryToast.ToastTone tone)
        {
            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;
            _toastMessage = message;
            _toastTone = tone;
            StateHasChanged();
            try
            {
                await Task.Delay(1500, token);
                if (!token.IsCancellationRequested)
                {
                    _toastMessage = null;
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException) { /* superseded */ }
        }

        private async Task TriggerInvalidShake()
        {
            _shakeCts?.Cancel();
            _shakeCts = new CancellationTokenSource();
            var token = _shakeCts.Token;
            _invalidTrace = true;
            StateHasChanged();
            try
            {
                await Task.Delay(450, token);
                if (!token.IsCancellationRequested)
                {
                    _invalidTrace = false;
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException) { /* superseded */ }
        }

        protected override void OnLobbyDisposing()
        {
            _toastCts?.Cancel();
            _shakeCts?.Cancel();
        }
    }
}
