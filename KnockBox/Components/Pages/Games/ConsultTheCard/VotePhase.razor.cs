using KnockBox.Services.Logic.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.ConsultTheCard
{
    public partial class VotePhase : ComponentBase
    {
        [Inject] protected ConsultTheCardGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<VotePhase> Logger { get; set; } = default!;

        [Parameter] public ConsultTheCardGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private string? _selectedTargetId;

        private ConsultTheCardPlayerState? GetMyPlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }

        private void SelectTarget(string targetId)
        {
            _selectedTargetId = targetId;
        }

        private void CancelSelection()
        {
            _selectedTargetId = null;
        }

        private void ConfirmVote()
        {
            if (UserService.CurrentUser == null || _selectedTargetId == null) return;

            var result = GameEngine.CastVote(UserService.CurrentUser, GameState, _selectedTargetId);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to cast vote: {Error}", error);
                _ = OnError.InvokeAsync("You cannot vote for that player.");
                _selectedTargetId = null;
            }
        }
    }
}
