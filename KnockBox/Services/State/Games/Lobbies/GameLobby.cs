using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.State.Games.Lobbies
{
    public abstract class GameLobby
    {
        private readonly Lock _lock = new();
        private readonly ILogger _logger;

        /// <summary>
        /// Invoked when this lobby is closed.
        /// </summary>
        public event Func<GameLobby, Task>? LobbyClosed;

        /// <summary>
        /// The unique key for this lobby.
        /// </summary>
        public readonly string RoomCode;

        /// <summary>
        /// Null when a game isn't selected.
        /// </summary>
        public GameType? GameType
        {
            get
            {
                using var scope = _lock.EnterScope();
                return field;
            }
            set
            {
                using var scope = _lock.EnterScope();
                if (value == field) return;
                field = value;

            }
        }

        private readonly List<Guid> _connectedUsers;
        public IReadOnlyList<Guid> ConnectedUsers
        {
            get 
            {
                using var scope = _lock.EnterScope();
                return _connectedUsers.ToArray();
            }
        }

        public GameLobby(string roomCode, ILogger logger)
        {
            RoomCode = roomCode;
            GameType = null;
            _logger = logger;
            _connectedUsers = [];
        }

        protected async Task NotifyLobbyClosureAsync()
        {
            async Task NotifyHandlerAsync(Func<GameLobby, Task> handler)
            {
                try
                {
                    await handler.Invoke(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying subscriber of lobby closure.");
                }
            }

            var tasks = LobbyClosed?.GetInvocationList()
                .Cast<Func<GameLobby, Task>>()
                .Select(NotifyHandlerAsync) ?? [];

            await Task.WhenAll(tasks);
        }

        public void RegisterUser(Guid user)
        {
            using var scope = _lock.EnterScope();
            _connectedUsers.Add(user);
        }

        public void UnregisterUser(Guid user)
        {
            using var scope = _lock.EnterScope();
            _connectedUsers.Remove(user);
        }
    }
}
