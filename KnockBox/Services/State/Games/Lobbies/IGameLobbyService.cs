using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.Lobbies
{
    /// <summary>
    /// A service used to create and join lobbies.
    /// </summary>
    /// <typeparam name="TLobby"></typeparam>
    public interface IGameLobbyService<TLobby>
        where TLobby : IGameLobby<TLobby>
    {
        /// <summary>
        /// Creates a new lobby for the game.
        /// </summary>
        /// <param name="userId">The id of the user creating the lobby.</param>
        /// <returns></returns>
        public ValueTask<Result<TLobby>> CreateLobbyAsync(Guid userId);

        /// <summary>
        /// Closes a lobby, ending the session.
        /// </summary>
        /// <param name="userId">The id of the user closing the lobby.</param>
        /// <returns></returns>
        public ValueTask<Result> CloseLobbyAsync(Guid userId);

        /// <summary>
        /// Joins the lobby.
        /// </summary>
        /// <param name="lobbyCode"></param>
        /// <returns></returns>
        public ValueTask<Result<TLobby>> JoinLobbyAsync(string lobbyCode, UserRegistration registration);
    }
}
