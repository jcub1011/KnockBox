using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Operator.Pages
{
    public partial class SetupPhase : ComponentBase
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<SetupPhase> Logger { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        protected async Task SubmitChoice(decimal choice)
        {
            if (UserService.CurrentUser == null) return;
            
            var command = new SubmitSetupChoiceCommand(UserService.CurrentUser.Id, choice);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);
            
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to submit setup choice: {Error}", error);
        }

        protected bool IsHost()
        {
            return UserService.CurrentUser?.Id == GameState.Host.Id;
        }

        protected bool ShouldShowWaiting()
        {
            if (UserService.CurrentUser == null) return false;
            if (IsHost()) return true;
            if (GameState.Context == null) return false;
            
            return GameState.Context.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var playerState)
                   && (playerState.CurrentPoints == GameState.Config.InitialPointsPositive
                       || playerState.CurrentPoints == GameState.Config.InitialPointsNegative);
        }
    }
}

