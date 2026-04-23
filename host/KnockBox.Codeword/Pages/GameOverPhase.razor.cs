using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword.Pages
{
    public partial class GameOverPhase : ComponentBase
    {
        [Inject] protected CodewordGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<GameOverPhase> Logger { get; set; } = default!;

        [Parameter] public CodewordGameState GameState { get; set; } = default!;

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

