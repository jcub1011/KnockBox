using KnockBox.Extensions.Returns;
using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.State.Games.Lobbies
{
    public interface ILobbyUriProvider
    {
        /// <summary>
        /// Registers the lobby and assigns it a unique lobby url.
        /// </summary>
        /// <remarks>
        /// Lobby Uris are formatted as "/room/{gameType}/{obfuscatedRoomCode}".
        /// </remarks>
        /// <param name="lobbyCode"></param>
        /// <param name="gameType"></param>
        /// <param name="lobbyUrl"></param>
        /// <returns>A <see cref="IDisposable"/> object that unregisters the lobby code when disposed.</returns>
        Result<IDisposable> RegisterLobby(string lobbyCode, GameType gameType, out string lobbyUrl);

        /// <summary>
        /// Gets the url assigned to the lobby code.
        /// </summary>
        /// <param name="lobbyCode"></param>
        /// <returns>Null when lobby isn't found.</returns>
        string? GetLobbyUri(string lobbyCode);
    }
}
