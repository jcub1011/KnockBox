using KnockBox.Services.State.Games.Lobbies;

namespace KnockBox.Services.Navigation.Games
{
    public interface ILobbyManager
    {
        /// <summary>
        /// Creates a room with the provided user as a host. Navigates the host to the lobby page.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask CreateRoomAsync(UserRegistration host, CancellationToken ct = default);

        /// <summary>
        /// Joins the room with the provided room code. Navigates the player to the lobby page.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask JoinRoomAsync(UserRegistration player, string roomCode, CancellationToken ct = default);

        /// <summary>
        /// Starts the room with the room code if the provided user is the host.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask StartGameAsync(UserRegistration host, string roomCode, CancellationToken ct = default);


    }
}
