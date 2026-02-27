using KnockBox.Extensions.ThreadSafety;
using KnockBox.Services.Navigation.Games;
using System.Collections.Concurrent;
using KnockBox.Extensions.Returns;
using KnockBox.Extensions.Events;
using KnockBox.Services.Logic.Games.Shared;

namespace KnockBox.Services.State.Games.Lobbies
{
    public enum LobbyState
    {
        /// <summary>
        /// The lobby is not initialized.
        /// </summary>
        Uninitialized,
        /// <summary>
        /// The lobby is open for players to join.
        /// </summary>
        Open,
        /// <summary>
        /// The lobby has a game active and cannot add players.
        /// </summary>
        Active,
        /// <summary>
        /// The lobby is closed and cannot add players.
        /// </summary>
        Closed
    }

    public abstract class GameLobby<TLobby>(ILogger logger, ILobbyCodeService lobbyCodeService)
        : IGameLobby<TLobby>, IAsyncDisposable
        where TLobby : GameLobby<TLobby>
    {
        private const string USER_CONNECTION_GROUP = "UserConnection";
        private const string LOBBY_STATE_GROUP = "LobbyState";
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly TypedThreadSafeEventManager _eventManager = new();
        private readonly ILobbyCodeService _lobbyCodeService = lobbyCodeService;
        private readonly ILogger _logger = logger;
        private LobbyState _state = LobbyState.Uninitialized;
        private bool _disposed = false;

        public event Func<TLobby, Task>? LobbyStateChanged;

        public string LobbyCode { get; private set; } = string.Empty;

        public LobbyState State
        {
            get => _lock.Read(in _state);
            protected set => _lock.Exchange(ref _state, value);
        }

        private readonly ConcurrentDictionary<Guid, UserRegistration> _connectedUsers = [];
        public IEnumerable<UserRegistration> ConnectedUsers => _connectedUsers.Values;
        public int UserCount => _connectedUsers.Count;

        public virtual async ValueTask<Result> InitializeRoomAsync(CancellationToken ct = default)
        {
            if (ValidateLobbyOpen().TryGetError(out var error)) return Result.FromError(error);
            if (State != LobbyState.Uninitialized)
                return Result.FromError(new InvalidOperationException($"Lobby [{LobbyCode}] is already initialized."));

            var result = await _lobbyCodeService.IssueLobbyCodeAsync(ct);
            if (result.TryGetValue(out var lobbyCode))
            {
                LobbyCode = lobbyCode;
                State = LobbyState.Open;
                return Result.Success;
            }
            else
            {
                _logger.LogError(result.Error, "Error initializing lobby.");
                return Result.FromError(result.Error);
            }
        }

        protected async Task<Result> NotifyLobbyClosureAsync()
        {
            if (ValidateLobbyOpen().TryGetError(out _)) return Result.Success;

            try
            {
                var previousState = _lock.Exchange(ref _state, LobbyState.Closed);
                var args = new LobbyStateChangeArgs<IGameLobby<TLobby>>(this, previousState, LobbyState.Closed);
                await _eventManager.NotifyAsync(LOBBY_STATE_GROUP, args);
                return Result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying subscribers of lobby closure.");
                return Result.FromError(ex);
            }
        }

        public Result ConnectUser(UserRegistration userRegistration)
        {
            if (ValidateLobbyOpen().TryGetError(out var error)) return Result.FromError(error);
            if (ValidateLobbyInitialized().TryGetError(out error)) return Result.FromError(error);

            if (userRegistration.Id == Guid.Empty)
                return Result.FromError(
                    new InvalidDataException($"User id [{userRegistration.Id}] is not valid."));

            if (_connectedUsers.TryAdd(userRegistration.Id, userRegistration))
            {
                return Result.Success;
            }
            else
            {
                return Result.FromError(
                    new InvalidOperationException($"User [{userRegistration.Id}] is already registered."));
            }
        }

        public Result DisconnectUser(UserRegistration userRegistration)
        {
            if (ValidateLobbyOpen().TryGetError(out var error)) return Result.FromError(error);
            if (ValidateLobbyInitialized().TryGetError(out error)) return Result.FromError(error);

            if (_connectedUsers.TryRemove(userRegistration.Id, out _))
            {
                return Result.Success;
            }
            else
            {
                return Result.FromError(
                    new InvalidOperationException($"User [{userRegistration.Id}] is not registered."));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            try
            {
                await NotifyLobbyClosureAsync();
            }
            finally
            {
                _lock.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        protected Result ValidateLobbyInitialized()
        {
            if (State == LobbyState.Uninitialized)
                return Result.FromError(new InvalidOperationException($"Lobby [{LobbyCode}] is not initialized."));

            return Result.Success;
        }

        protected Result ValidateLobbyOpen()
        {
            if (_state == LobbyState.Open)
                return Result.FromError(new InvalidOperationException($"Lobby [{LobbyCode}] is {_state}."));
            if (_disposed)
                return Result.FromError(new ObjectDisposedException($"Lobby [{LobbyCode}] is disposed."));

            return Result.Success;
        }

        public void Subscribe<TType>(string evt, Func<TType, ValueTask> callback)
        {
            _eventManager.Subscribe(evt, callback);
        }

        public void Unsubscribe<TType>(string evt, Func<TType, ValueTask> callback)
        {
            _eventManager.Unsubscribe(evt, callback);
        }

        public async ValueTask<Result> StartRoomAsync(CancellationToken ct = default)
        {
            var previousState = _lock.Exchange(ref _state, LobbyState.Active);
            var args = new LobbyStateChangeArgs<IGameLobby<TLobby>>(this, previousState, LobbyState.Active);

            try
            {
                await _eventManager.NotifyAsync(LOBBY_STATE_GROUP, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying subscribers that room has become active.");
                return Result.FromError(ex);
            }

            return Result.Success;
        }

        public void SubscribeToLobbyStateChanges(Func<LobbyStateChangeArgs<TLobby>, ValueTask> callback)
        {
            _eventManager.Subscribe(LOBBY_STATE_GROUP, callback);
        }

        public void UnsubscribeFromLobbyStateChanges(Func<LobbyStateChangeArgs<TLobby>, ValueTask> callback)
        {
            _eventManager.Unsubscribe(LOBBY_STATE_GROUP, callback);
        }

        public void SubscribeToUserConnection(Func<UserConnectionArgs<TLobby>, ValueTask> callback)
        {
            _eventManager.Subscribe(USER_CONNECTION_GROUP, callback);
        }

        public void UnsubscribeFromUserConnection(Func<UserConnectionArgs<TLobby>, ValueTask> callback)
        {
            _eventManager.Unsubscribe(USER_CONNECTION_GROUP, callback);
        }
    }
}
