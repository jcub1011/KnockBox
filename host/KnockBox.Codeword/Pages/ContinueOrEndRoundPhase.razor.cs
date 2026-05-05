using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword.Pages
{
    public partial class ContinueOrEndRoundPhase : ComponentBase
    {
        [Inject] protected CodewordGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<ContinueOrEndRoundPhase> Logger { get; set; } = default!;

        [Parameter] public CodewordGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private CodewordPlayerState? GetMyPlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }

        private void Vote(bool voteToEnd)
        {
            if (UserService.CurrentUser == null) return;

            var result = GameEngine.VoteContinueOrEndRound(UserService.CurrentUser, GameState, voteToEnd);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to cast continue/end vote: {Error}", error);
                _ = OnError.InvokeAsync("Vote not accepted.");
            }
        }
    }
}
