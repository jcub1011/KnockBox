using KnockBox.Extensions.ThreadSafety;
using KnockBox.Extensions.Collections;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.State.Games.Lobbies
{
    public record class UserRegistration(string Name, Guid Id);

    public abstract class GameLobby<TLobby> 
        : IDisposable
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

        private readonly List<UserRegistration> _connectedUsers;
        public IReadOnlyList<UserRegistration> ConnectedUsers
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

        public void RegisterUser(IUserService user)
        {
            using var scope = _lock.EnterWriteScope();

            if (!_connectedUsers.Any(u => u.Id == user.UserId))
                _connectedUsers.Add(new(user.UserName, user.UserId));
        }

        public void UnregisterUser(IUserService user)
        {
            using var scope = _lock.EnterWriteScope();

            _connectedUsers.Remove(u => u.Id == user.UserId);
        }

        public void Dispose()
        {
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
