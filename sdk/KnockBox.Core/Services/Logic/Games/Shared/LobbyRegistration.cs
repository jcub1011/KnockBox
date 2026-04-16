using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Core.Services.Logic.Games.Shared
{
    public class LobbyRegistration(string lobbyCode, string lobbyUri, string gameName, string routeIdentifier, AbstractGameState state)
    {
        /// <summary>
        /// The code for this lobby.
        /// </summary>
        public string Code { get; } = lobbyCode;

        /// <summary>
        /// The uri for this lobby. Formatted as "room/{routeIdentifier}/{obfuscatedRoomCode}".
        /// </summary>
        public string Uri { get; } = lobbyUri;

        /// <summary>
        /// The name of the game.
        /// </summary>
        public string GameName { get; } = gameName;

        /// <summary>
        /// The route identifier for this game.
        /// </summary>
        public string RouteIdentifier { get; } = routeIdentifier;

        /// <summary>
        /// The game state for this lobby.
        /// </summary>
        public AbstractGameState State { get; } = state;
    }

    public record class UserRegistration(User User, IDisposable UnregistrationToken, LobbyRegistration LobbyRegistration) : IDisposable
    {
        public void Dispose() => UnregistrationToken.Dispose();
    }
}
