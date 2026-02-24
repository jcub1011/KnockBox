using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.Lobbies
{
    public record class UserRegistration(string Name, Guid Id);
    public record class LobbyStateChangeArgs<TLobby>(TLobby Lobby, LobbyState PreviousState, LobbyState CurrentState);
    public record class UserConnectionArgs<TLobby>(TLobby Lobby, UserRegistration UserRegistration, bool UserConnected);

    public interface IGameLobby<TLobby>
        where TLobby : IGameLobby<TLobby>
    {
        /// <summary>
        /// The unique key for this lobby.
        /// </summary>
        public string LobbyCode { get; }

        /// <summary>
        /// The current state of the lobby.
        /// </summary>
        public LobbyState State { get; }

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
        /// Starts the room, transitioning it to the active state.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<Result> StartRoomAsync(CancellationToken ct = default);

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

        /// <summary>
        /// Subscribes to lobby state changed events.
        /// </summary>
        /// <param name="callback"></param>
        public void SubscribeToLobbyStateChanges(Func<LobbyStateChangeArgs<TLobby>, ValueTask> callback);

        /// <summary>
        /// Unsubscribes from lobby state changed events.
        /// </summary>
        /// <param name="callback"></param>
        public void UnsubscribeFromLobbyStateChanges(Func<LobbyStateChangeArgs<TLobby>, ValueTask> callback);

        /// <summary>
        /// Subscribes to user connection events.
        /// </summary>
        /// <param name="callback"></param>
        public void SubscribeToUserConnection(Func<UserConnectionArgs<TLobby>, ValueTask> callback);

        /// <summary>
        /// Unsubscribes from user connection events.
        /// </summary>
        /// <param name="callback"></param>
        public void UnsubscribeFromUserConnection(Func<UserConnectionArgs<TLobby>, ValueTask> callback);

        /// <summary>
        /// Subscribes to the event.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="evt"></param>
        /// <param name="callback"></param>
        public void Subscribe<TType>(string evt, Func<TType, ValueTask> callback);

        /// <summary>
        /// Unsubscribes from the event.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="evt"></param>
        /// <param name="callback"></param>
        public void Unsubscribe<TType>(string evt, Func<TType, ValueTask> callback);
    }
}
