using KnockBox.Extensions.Returns;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.Shared
{
    public class LobbyRegistration(string lobbyCode, string lobbyUri, GameType gameType, User host)
    {
        /// <summary>
        /// The lobby code for this lobby.
        /// </summary>
        public readonly string LobbyCode = lobbyCode;

        /// <summary>
        /// The uri for this lobby.
        /// </summary>
        public readonly string LobbyUri = lobbyUri;

        /// <summary>
        /// The type of the lobby.
        /// </summary>
        public readonly GameType GameType = gameType;

        /// <summary>
        /// The host for the lobby.
        /// </summary>
        public User Host { get; set; } = host;
    }

    public interface ILobbyService
    {
        /// <summary>
        /// Creates a lobby with the provided user as host and game type.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="gameType"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<Result<LobbyRegistration>> CreateLobbyAsync(User host, GameType gameType, CancellationToken ct = default);

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
        Task<Result<LobbyRegistration>> JoinLobbyAsync(User user, string lobbyCode, CancellationToken ct = default);

        /// <summary>
        /// Leaves the lobby.
        /// </summary>
        /// <param name="user">The user to leave. Cannot be the host.</param>
        /// <param name="registration"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<Result> LeaveLobbyAsync(User user, LobbyRegistration registration, CancellationToken ct = default);

        /// <summary>
        /// Reassigns the host for the lobby.
        /// </summary>
        /// <param name="previousHost"></param>
        /// <param name="newHost"></param>
        /// <param name="registration"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<Result> ReassignHostAsync(User previousHost, User newHost, LobbyRegistration registration, CancellationToken ct = default);
    }
}
