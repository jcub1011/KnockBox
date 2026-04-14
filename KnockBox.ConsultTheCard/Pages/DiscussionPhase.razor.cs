using KnockBox.ConsultTheCard.Services.Logic.Games;
using KnockBox.ConsultTheCard.Services.State.Games;
using KnockBox.ConsultTheCard.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.ConsultTheCard.Pages
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
                _ = OnError.InvokeAsync("Action not available right now.");
            }
        }

        protected void SkipRemainingTime()
        {
            if (UserService.CurrentUser == null) return;

            var result = GameEngine.SkipRemainingTime(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to skip remaining time: {Error}", error);
                _ = OnError.InvokeAsync("Action not available right now.");
            }
        }

        protected void SelectTarget(string targetId)
        {
            if (UserService.CurrentUser == null) return;

            var myPlayer = GetMyPlayer();
            if (myPlayer is not null && myPlayer.HasVoted) return;

            var result = GameEngine.CastVote(UserService.CurrentUser, GameState, targetId);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to select vote target: {Error}", error);
                _ = OnError.InvokeAsync("You cannot vote for that player.");
            }
        }

        protected void ConfirmVote()
        {
            if (UserService.CurrentUser == null) return;

            var result = GameEngine.LockInVote(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to lock in vote: {Error}", error);
                _ = OnError.InvokeAsync("Please select a player before locking in.");
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

