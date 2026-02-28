using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Shared;

namespace KnockBox.Services.Navigation
{
    public interface INavigationService
    {
        /// <summary>
        /// The base of the game route.
        /// </summary>
        string GameBaseRoute { get; }

        /// <summary>
        /// Gets the absolute uri for the home page.
        /// </summary>
        /// <returns></returns>
        string GetHomeUri();

        /// <summary>
        /// Navigates to the home page.
        /// </summary>
        void ToHome();

        /// <summary>
        /// Returns the absolute uri for the provided game lobby.
        /// </summary>
        /// <param name="lobbyRegistration"></param>
        /// <returns></returns>
        string GetGameUri(LobbyRegistration lobbyRegistration);

        /// <summary>
        /// Navigates to the lobby of the provided game.
        /// </summary>
        /// <param name="lobbyRegistration"></param>
        void ToGame(LobbyRegistration lobbyRegistration);
    }
}
