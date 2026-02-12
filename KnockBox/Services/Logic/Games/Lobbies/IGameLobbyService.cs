using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.Logic.Games.Lobbies
{
    /// <summary>
    /// Represents a game lobby with a unique room code and an optional game type.
    /// </summary>
    /// <param name="RoomCode">The unique code identifying the game lobby. Cannot be null or empty.</param>
    /// <param name="GameType">The type of game associated with the lobby, or null if not specified.</param>
    public record class GameLobby(string RoomCode, GameType? GameType);

    public record class LobbyFilter(GameType? Type);

    public interface IGameLobbyService
    {
        /// <summary>
        /// Gets all the active lobbies in this server.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<List<GameLobby>> GetLobbiesAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets all the active lobbies in this server matching the filter.
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<List<GameLobby>> GetLobbiesAsync(LobbyFilter filter, CancellationToken ct = default);

        /// <summary>
        /// Gets the lobby with the room code.
        /// </summary>
        /// <param name="roomCode"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<GameLobby?> GetLobbyAsync(string roomCode, CancellationToken ct = default);

        /// <summary>
        /// Creates a lobby of the specified type.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<GameLobby> CreateLobbyAsync(GameType type, CancellationToken ct = default);

        /// <summary>
        /// Ends the lobby with the room code.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task EndLobbyAsync(string roomCode, CancellationToken ct = default);
    }
}
