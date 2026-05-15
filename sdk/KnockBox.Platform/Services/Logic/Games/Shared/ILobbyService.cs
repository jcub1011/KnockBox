using System.Diagnostics.CodeAnalysis;
using KnockBox.Core.Primitives.Returns;
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

        /// <summary>
        /// Looks up a lobby by its full obfuscated URI
        /// (<c>room/{routeIdentifier}/{guidA}-{guidB}</c>). Used by the plugin
        /// HTTP dispatcher to resolve a room from a request path; the URI's two
        /// random GUIDs serve as the access token for that endpoint family.
        /// </summary>
        /// <param name="uri">The full lobby URI as stored on <see cref="LobbyRegistration.Uri"/>.</param>
        /// <param name="registration">The matching registration on success; <c>null</c> on miss.</param>
        /// <returns><c>true</c> if a lobby with the given URI exists; otherwise <c>false</c>.</returns>
        bool TryGetByUri(string uri, [NotNullWhen(true)] out LobbyRegistration? registration);
    }
}
