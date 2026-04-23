using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.CardCounter.Pages
{
    public partial class GameOverPhase : ComponentBase
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<GameOverPhase> Logger { get; set; } = default!;

        [Parameter] public CardCounterGameState GameState { get; set; } = default!;

        protected bool IsHost()
        {
            if (UserService.CurrentUser == null) return false;
            return GameState.Host.Id == UserService.CurrentUser.Id;
        }

        protected void ResetGame()
        {
            if (UserService.CurrentUser == null) return;
            var result = GameEngine.ResetGame(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to reset game: {Error}", error);
        }

        protected void ReturnToLobby()
        {
            if (UserService.CurrentUser == null) return;
            var result = GameEngine.ReturnToLobby(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to return to lobby: {Error}", error);
        }
    }
}

