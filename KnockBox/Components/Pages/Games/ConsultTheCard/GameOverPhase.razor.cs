using KnockBox.Services.Logic.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.ConsultTheCard
{
    public partial class GameOverPhase : ComponentBase
    {
        [Inject] protected ConsultTheCardGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<GameOverPhase> Logger { get; set; } = default!;

        [Parameter] public ConsultTheCardGameState GameState { get; set; } = default!;

        protected static string GetTeamName(Role? team) => team switch
        {
            Role.Agent => "Agents",
            Role.Insider => "Insiders",
            Role.Informant => "Informant",
            _ => "Unknown"
        };

        protected void StartNextGame()
        {
            if (UserService.CurrentUser == null) return;
            var result = GameEngine.StartNextGame(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start next game: {Error}", error);
        }

        protected void ReturnToLobby()
        {
            if (UserService.CurrentUser == null) return;
            var result = GameEngine.ReturnToLobby(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to return to lobby: {Error}", error);
        }

        protected void ResetGame()
        {
            if (UserService.CurrentUser == null) return;
            var result = GameEngine.ResetGame(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to reset game: {Error}", error);
        }
    }
}
