using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.Navigation
{
    public interface INavigationService
    {
        /// <summary>
        /// The base of the game route.
        /// </summary>
        string GameBaseRoute { get; }

        /// <summary>
        /// Returns the absolute uri for the provided game lobby.
        /// </summary>
        /// <param name="gameType"></param>
        /// <returns></returns>
        string GetGameUri(GameType gameType);

        /// <summary>
        /// Navigates to the lobby of the provided game.
        /// </summary>
        /// <param name="gameType"></param>
        void ToGame(GameType gameType);
    }
}
