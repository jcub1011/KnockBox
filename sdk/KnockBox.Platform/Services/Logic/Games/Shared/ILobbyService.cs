using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Platform.Games
{
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

        /// <summary>
        /// Returns the number of currently-active lobbies keyed by game route
        /// identifier. Used by the admin dashboard to surface "games running
        /// per type". Games with zero active lobbies are not included; callers
        /// should union with the full plugin list if they want a complete table.
        /// </summary>
        IReadOnlyDictionary<string, int> GetLobbyCountsByRoute();
    }
}
