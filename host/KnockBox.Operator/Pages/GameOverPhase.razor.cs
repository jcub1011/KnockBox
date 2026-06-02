using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Operator.Pages
{
    public partial class GameOverPhase : ComponentBase
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<GameOverPhase> Logger { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        protected bool IsHost()
        {
            return UserService.CurrentUser?.Id == GameState.Host.Id;
        }

        private void ReturnToLobby()
        {
            if (UserService.CurrentUser is null) return;
            var result = GameEngine.ReturnToLobby(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to return to lobby: {Error}", error);
        }
    }
}

