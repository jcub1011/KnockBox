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
}
