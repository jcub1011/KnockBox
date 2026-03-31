using KnockBox.Services.Logic.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.ConsultTheCard
{
    public partial class DiscussionPhase : ComponentBase
    {
        [Inject] protected ConsultTheCardGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<DiscussionPhase> Logger { get; set; } = default!;

        [Parameter] public ConsultTheCardGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private ConsultTheCardPlayerState? GetMyPlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }

        protected void VoteToEndGame()
        {
            if (UserService.CurrentUser == null) return;

            var result = GameEngine.VoteToEndGame(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to vote to end game: {Error}", error);
                _ = OnError.InvokeAsync("You have already voted to end the game this round.");
            }
        }

        protected void AdvanceToVote()
        {
            if (UserService.CurrentUser == null) return;

            var result = GameEngine.AdvanceToVote(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to advance to vote: {Error}", error);
                _ = OnError.InvokeAsync("Action not available right now.");
            }
        }
    }
}
