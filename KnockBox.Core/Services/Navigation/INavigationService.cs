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
        /// Returns the absolute uri for the join page with the provided room code.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="fresh">Whether to force a fresh session (new user id).</param>
        /// <returns></returns>
        string GetJoinUri(string code, bool fresh = false);

        /// <summary>
        /// Navigates to the lobby of the provided game.
        /// </summary>
        /// <param name="lobbyRegistration"></param>
        void ToGame(LobbyRegistration lobbyRegistration);
    }
}
