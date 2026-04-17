using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Navigation;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Services.Navigation
{
    /// <summary>
    /// Default <see cref="INavigationService"/>. Wraps ASP.NET Core's
    /// <see cref="NavigationManager"/> so game pages can navigate to typed
    /// platform routes (<c>home</c>, game URIs, join URIs) without stitching
    /// URL strings together themselves. Registered as scoped.
    /// </summary>
    internal sealed class NavigationService(NavigationManager navigationManager)
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

        public string GetJoinUri(string code, bool fresh = false)
        {
            var uri = $"{GetHomeUri()}?join={code}";
            if (fresh)
            {
                uri += "&fresh=1";
            }
            return uri;
        }

        public void ToGame(LobbyRegistration lobbyRegistration)
        {
            navigationManager.NavigateTo(GetGameUri(lobbyRegistration));
        }
    }
}
