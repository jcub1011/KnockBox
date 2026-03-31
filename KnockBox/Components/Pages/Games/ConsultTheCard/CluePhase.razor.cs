using KnockBox.Services.Logic.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.ConsultTheCard
{
    public partial class CluePhase : ComponentBase
    {
        [Inject] protected ConsultTheCardGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<CluePhase> Logger { get; set; } = default!;

        [Parameter] public ConsultTheCardGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private string _clueText = string.Empty;

        protected void OnClueInput(ChangeEventArgs e)
        {
            _clueText = e.Value?.ToString() ?? string.Empty;

            // Store pending clue on player state so the server can auto-submit on timeout.
            var myId = UserService.CurrentUser?.Id;
            if (myId is not null && GameState.GamePlayers.TryGetValue(myId, out var player) && !player.HasSubmittedClue)
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
            }
        }
    }
}
