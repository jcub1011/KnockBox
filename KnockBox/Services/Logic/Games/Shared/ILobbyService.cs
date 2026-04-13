using KnockBox.Extensions.Returns;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.Shared
{
    public class LobbyRegistration(string lobbyCode, string lobbyUri, string gameName, string routeIdentifier, AbstractGameState state)
    {
        /// <summary>
        /// The code for this lobby.
        /// </summary>
        public readonly string Code = lobbyCode;

        /// <summary>
        /// The uri for this lobby. Formatted as "room/{gameType}/{obfuscatedRoomCode}".
        /// </summary>
        public readonly string Uri = lobbyUri;

        /// <summary>
        /// The name of the game.
        /// </summary>
        public readonly string GameName = gameName;

        /// <summary>
        /// The route identifier for this game.
        /// </summary>
        public readonly string RouteIdentifier = routeIdentifier;

        /// <summary>
        /// The game state for this lobby.
        /// </summary>
        public readonly AbstractGameState State = state;
    }

    public record class UserRegistration(User User, IDisposable UnregistrationToken, LobbyRegistration LobbyRegistration) : IDisposable
    {
        public void Dispose() => UnregistrationToken.Dispose();
    }

    public interface ILobbyService
    {
        /// <summary>
        /// Creates a lobby with the provided user as host and game route identifier.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="routeIdentifier"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<ValueResult<LobbyRegistration>> CreateLobbyAsync(User host, string routeIdentifier, CancellationToken ct = default);

        /// <summary>
        /// Closes the lobby.
        /// </summary>
        /// <param name="user">Only succeeds when the user is the host.</param>
        /// <param name="registration">The lobby to close.</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<Result> CloseLobbyAsync(User user, LobbyRegistration registration, CancellationToken ct = default);

        /// <summary>
        /// Joins the lobby.
        /// </summary>
        /// <param name="user">The user to join. Cannot be the host.</param>
        /// <param name="lobbyCode">The code for the lobby to join.</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<ValueResult<UserRegistration>> JoinLobbyAsync(User user, string lobbyCode, CancellationToken ct = default);
    }
}
