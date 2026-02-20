using KnockBox.Extensions.Exceptions;
using KnockBox.Extensions.Returns;
using KnockBox.Extensions.ThreadSafety;

namespace KnockBox.Services.State.Games.Lobbies
{
    internal record class LobbyRegistration<TLobby>(TLobby Lobby, Guid CreatorId);

    /// <summary>
    /// The default implementation of a game lobby service.
    /// </summary>
    /// <typeparam name="TGameLobby"></typeparam>
    public class BaseGameLobbyService<TGameLobby>(
        IServiceProvider serviceProvider, 
        ILogger<BaseGameLobbyService<TGameLobby>> logger)
        : IGameLobbyService<TGameLobby>, IAsyncDisposable
        where TGameLobby : IGameLobby<TGameLobby>
    {
        private bool _disposed = false;
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly Dictionary<string, AsyncServiceScope> _lobbyScopes = [];
        private readonly Dictionary<string, LobbyRegistration<TGameLobby>> _lobbies = [];
        private readonly Dictionary<Guid, LobbyRegistration<TGameLobby>> _userLobbyMap = [];

        public async ValueTask<Result> CloseLobbyAsync(Guid userId, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);

                using var scope = _lock.EnterWriteScope();

                if (!_userLobbyMap.TryGetValue(userId, out var registration))
                    return Result.FromError(
                        new InvalidOperationException($"User [{userId}] is not the host of a lobby."));

                return await CloseLobbyAsync(registration, ct);
            }
            catch (Exception ex)
            {
                return Result.FromError(ex);
            }
        }

        private async ValueTask<Result> CloseLobbyAsync(
            LobbyRegistration<TGameLobby> registration, 
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            string roomCode = registration.Lobby.LobbyCode;

            if (!_lobbies.Remove(roomCode))
                return Result.FromError(
                    new InvalidOperationException($"User [{registration.CreatorId}] had lobby registration but registration didn't exist."));

            if (_lobbyScopes.Remove(roomCode, out var lobbyScope))
                await lobbyScope.DisposeAsync();
            else return Result.FromError(
                new InvalidOperationException($"Unable to find scope for lobby [{roomCode}]."));

            return Result.Success;
        }

        public async ValueTask<Result<TGameLobby>> CreateLobbyAsync(Guid userId, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);

                var serviceScope = serviceProvider.CreateAsyncScope();
                var lobby = serviceScope.ServiceProvider.GetService<TGameLobby>();
                if (lobby is null)
                {
                    var typeName = typeof(TGameLobby).Name;
                    return Result.FromError<TGameLobby>(
                        new InvalidOperationException($"Service [{typeName}] is not registered."));
                }

                var registration = new LobbyRegistration<TGameLobby>(lobby, userId);

                using var scope = _lock.EnterWriteScope();
                if (_userLobbyMap.TryGetValue(userId, out var existingLobby))
                {
                    var lobbyCode = existingLobby.Lobby.LobbyCode;
                    return Result.FromError<TGameLobby>(
                        new InvalidOperationException($"User [{userId}] is already the host of a lobby [{lobbyCode}]."));
                }

                _userLobbyMap[userId] = registration;
                _lobbies[registration.Lobby.LobbyCode] = registration;

                return new Result<TGameLobby>(registration.Lobby);
            }
            catch (Exception ex)
            {
                return Result.FromError<TGameLobby>(ex);
            }
        }

        public async ValueTask<Result<TGameLobby>> JoinLobbyAsync(
            string lobbyCode, 
            UserRegistration registration, 
            CancellationToken ct = default)
        {
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var scope = _lock.EnterReadScope();
                if (_lobbies.TryGetValue(lobbyCode, out var lobby))
                {
                    lobby.Lobby.ConnectUser(registration);
                    return Result.FromValue(lobby.Lobby);
                }

                return Result.FromError<TGameLobby>(new LobbyNotFoundException(lobbyCode));
            }
            catch (Exception ex)
            {
                return Result.FromError<TGameLobby>(ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            using var scope = _lock.EnterWriteScope();
            foreach (var registration in _lobbies.Values)
            {
                if ((await CloseLobbyAsync(registration, CancellationToken.None)).TryGetError(out var error))
                {
                    logger.LogError(error, "Error closing lobby [{code}].", registration.Lobby.LobbyCode);
                }
            }

            _lock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
