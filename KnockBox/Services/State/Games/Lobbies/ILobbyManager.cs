using KnockBox.Extensions.Returns;
using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.State.Games.Lobbies
{
    public interface ILobbyManager
    {
        /// <summary>
        /// Creates a new lobby of the specified type. Makes the provided user the host.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="ct"></param>
        /// <returns>The uri for the lobby.</returns>
        ValueTask<Result<string>> CreateLobbyAsync(UserRegistration user, GameType gameType, CancellationToken ct = default);

        /// <summary>
        /// Joins a lobby with the specified lobby code.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="ct"></param>
        /// <returns>The uri for the lobby.</returns>
        ValueTask<Result<string>> JoinLobbyAsync(UserRegistration user, string lobbyCode, CancellationToken ct = default);

        /// <summary>
        /// Starts the lobby that the user is the host of.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<Result> StartLobbyAsync(UserRegistration user, CancellationToken ct = default);
    }
}
