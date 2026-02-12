using KnockBox.Components.Shared;
using KnockBox.Services.Navigation;
using KnockBox.Services.Navigation.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Home
{
    public partial class Home : DisposableComponent
    {
        [Inject] INavigationService NavigationService { get; set; } = default!;

        private void NavigateToGame(GameType gameType)
        {
            NavigationService.ToGame(gameType);
        }
    }
}
