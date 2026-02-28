using KnockBox.Services.Logic.Games.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Services.Navigation
{
    public class NavigationService(NavigationManager navigationManager)
        : INavigationService
    {
        public string GameBaseRoute => "games";

        public string GetHomeUri()
        {
            return $"{navigationManager.BaseUri}home";
        }

        public void ToHome()
        {
            navigationManager.NavigateTo(GetHomeUri());
        }

        public string GetGameUri(LobbyRegistration lobbyRegistration)
        {
            return $"{navigationManager.BaseUri}{lobbyRegistration.Uri}";
        }

        public void ToGame(LobbyRegistration lobbyRegistration)
        {
            navigationManager.NavigateTo(GetGameUri(lobbyRegistration));
        }
    }
}
