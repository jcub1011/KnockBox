using KnockBox.Extensions.ThreadSafety;
using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.State.Games.Lobbies
{
    public abstract class GameLobby<TLobby>
        where TLobby : GameLobby<TLobby>
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly ILogger _logger;

        /// <summary>
        /// Invoked when this lobby is closed.
        /// </summary>
        public event Func<TLobby, Task>? LobbyClosed;

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
                using var scope = _lock.EnterReadScope();
                return field;
            }
            set
            {
                using var scope = _lock.EnterWriteScope();
                if (value == field) return;
                field = value;

            }
        }

        private readonly List<Guid> _connectedUsers;
        public IReadOnlyList<Guid> ConnectedUsers
        {
            get 
            {
                using var scope = _lock.EnterReadScope();
                return [.. _connectedUsers];
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
            async Task NotifyHandlerAsync(Func<TLobby, Task> handler)
            {
                try
                {
                    await handler.Invoke((TLobby)this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying subscriber of lobby closure.");
                }
            }

            Delegate[]? recipients;
            using (var scope = _lock.EnterReadScope())
            {
                recipients = LobbyClosed?.GetInvocationList();
            }

            var tasks = recipients?
                .Cast<Func<TLobby, Task>>()
                .Select(NotifyHandlerAsync) ?? [];

            await Task.WhenAll(tasks);
        }

        public void RegisterUser(Guid user)
        {
            using var scope = _lock.EnterWriteScope();
            _connectedUsers.Add(user);
        }

        public void UnregisterUser(Guid user)
        {
            using var scope = _lock.EnterWriteScope();
            _connectedUsers.Remove(user);
        }
    }
}
