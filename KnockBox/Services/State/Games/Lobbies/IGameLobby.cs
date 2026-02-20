using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.Lobbies
{
    public record class UserRegistration(string Name, Guid Id);

    public interface IGameLobby<TLobby>
        where TLobby : IGameLobby<TLobby>
    {
        /// <summary>
        /// Invoked when this lobby is closed.
        /// </summary>
        public event Func<TLobby, Task>? LobbyClosed;

        /// <summary>
        /// The unique key for this lobby.
        /// </summary>
        public string LobbyCode { get; }

        /// <summary>
        /// If this lobby is initialized.
        /// </summary>
        public bool IsInitialized { get; }

        /// <summary>
        /// The users in this lobby.
        /// </summary>
        public IEnumerable<UserRegistration> ConnectedUsers { get; }

        /// <summary>
        /// The number of users in this lobby.
        /// </summary>
        public int UserCount { get; }

        /// <summary>
        /// Initializes the room so that users can begin connecting.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<Result> InitializeRoomAsync(CancellationToken ct = default);

        /// <summary>
        /// Connects the user to the lobby.
        /// </summary>
        /// <param name="registration"></param>
        /// <returns></returns>
        public Result ConnectUser(UserRegistration registration);

        /// <summary>
        /// Disconnects the user from the lobby.
        /// </summary>
        /// <param name="registration"></param>
        /// <returns></returns>
        public Result DisconnectUser(UserRegistration registration);
    }
}
