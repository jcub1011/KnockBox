using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.Lobbies
{
    /// <summary>
    /// A service used to create and join lobbies.
    /// </summary>
    /// <typeparam name="TLobby"></typeparam>
    public interface IGameLobbyService<TLobby>
        where TLobby : GameLobby<TLobby>
    {
        /// <summary>
        /// Creates a new lobby for the game.
        /// </summary>
        /// <returns></returns>
        public Task<Result<TLobby>> CreateLobbyAsync();

        /// <summary>
        /// Closes a lobby, ending the session.
        /// </summary>
        /// <returns></returns>
        public Task<Result> CloseLobbyAsync();

        /// <summary>
        /// Joins the lobby.
        /// </summary>
        /// <param name="lobbyCode"></param>
        /// <returns></returns>
        public Task<Result<TLobby>> JoinLobbyAsync(string lobbyCode);
    }
}
