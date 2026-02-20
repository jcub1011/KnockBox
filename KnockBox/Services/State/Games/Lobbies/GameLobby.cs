using KnockBox.Extensions.ThreadSafety;
using KnockBox.Services.Navigation.Games;
using System.Collections.Concurrent;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Lobbies;

namespace KnockBox.Services.State.Games.Lobbies
{
    public abstract class GameLobby<TLobby> 
        : IGameLobby<TLobby>, IAsyncDisposable
        where TLobby : GameLobby<TLobby>
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly ILogger _logger;
        private readonly ILobbyCodeService _lobbyCodeService;
        private bool _closed = false;
        private bool _disposed = false;

        public event Func<TLobby, Task>? LobbyClosed;

        public string LobbyCode { get; private set; }

        public bool IsInitialized => string.IsNullOrEmpty(LobbyCode);

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

        private readonly ConcurrentDictionary<Guid, UserRegistration> _connectedUsers = [];
        public IEnumerable<UserRegistration> ConnectedUsers => _connectedUsers.Values;
        public int UserCount => _connectedUsers.Count;

        public GameLobby(ILogger logger, ILobbyCodeService lobbyCodeService)
        {
            _lobbyCodeService = lobbyCodeService;
            _logger = logger;
            LobbyCode = string.Empty;
            GameType = null;
        }

        public virtual async ValueTask<Result> InitializeRoomAsync(CancellationToken ct = default)
        {
            if (ValidateLobbyOpen().TryGetError(out var error)) return Result.FromError(error);
            if (IsInitialized)
                return Result.FromError(new InvalidOperationException($"Lobby [{LobbyCode}] is already initialized."));

            var result = await _lobbyCodeService.IssueLobbyCodeAsync(ct);
            if (result.TryGetValue(out var lobbyCode))
            {
                LobbyCode = lobbyCode;
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

            try
            {
                Delegate[]? recipients;
                using (var scope = _lock.EnterReadScope())
                {
                    recipients = LobbyClosed?.GetInvocationList();
                }

                var tasks = recipients?
                    .Cast<Func<TLobby, Task>>()
                    .Select(NotifyHandlerAsync) ?? [];

                await Task.WhenAll(tasks);

                _closed = true;
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
            if (!IsInitialized)
                return Result.FromError(new InvalidOperationException($"Lobby [{LobbyCode}] is not initialized."));

            return Result.Success;
        }

        protected Result ValidateLobbyOpen()
        {
            if (_closed)
                return Result.FromError(new InvalidOperationException($"Lobby [{LobbyCode}] is closed."));
            if (_disposed)
                return Result.FromError(new ObjectDisposedException($"Lobby [{LobbyCode}] is disposed."));

            return Result.Success;
        }
    }
}
