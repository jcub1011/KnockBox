using KnockBox.Services.Navigation.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Services.Navigation
{
    public class NavigationService(NavigationManager navigationManager)
        : INavigationService
    {
        public string GameBaseRoute => "games";

        public string GetGameUri(GameType gameType)
        {
            string gameNavString = NavigationString.GetNavigationStringAttribute(gameType)
                ?? throw new NavigationException($"{nameof(NavigationString)} not found for {nameof(GameType)} '{gameType}'.");

            return $"{navigationManager.BaseUri}{GameBaseRoute}/{gameNavString}/lobby";
        }

        public void ToGame(GameType gameType)
        {
            navigationManager.NavigateTo(GetGameUri(gameType));
        }
    }
}
