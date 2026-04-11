using KnockBox.Operator.Services.State;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.Operator
{
    public partial class GameOverPhase : ComponentBase
    {
        [Inject] protected INavigationService NavigationService { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        protected bool IsHost()
        {
            return UserService.CurrentUser?.Id == GameState.Host.Id;
        }

        private void GoHome()
        {
            NavigationService.ToHome();
        }
    }
}
