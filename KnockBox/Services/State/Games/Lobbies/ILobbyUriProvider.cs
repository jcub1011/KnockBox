using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.State.Games.Lobbies
{
    public interface ILobbyUriProvider
    {
        /// <summary>
        /// Registers the lobby and assigns it a unique lobby url.
        /// </summary>
        /// <remarks>
        /// Lobby uris are formatted as "/room/{gameType}/{obfuscatedRoomCode}".
        /// </remarks>
        /// <param name="lobbyCode"></param>
        /// <param name="gameType"></param>
        /// <returns>A <see cref="LobbyRegistration"/> object that unregisters the lobby when the Unsubscriber is disposed.</returns>
        Result<LobbyRegistration> RegisterLobby(string lobbyCode, GameType gameType);

        /// <summary>
        /// Gets the url assigned to the lobby code.
        /// </summary>
        /// <param name="lobbyCode"></param>
        /// <returns>Null when lobby isn't found.</returns>
        string? GetLobbyUri(string lobbyCode);
    }
}
