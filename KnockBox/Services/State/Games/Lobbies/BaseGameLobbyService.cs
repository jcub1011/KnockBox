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
    public class BaseGameLobbyService<TGameLobby>(IServiceProvider serviceProvider)
        : IGameLobbyService<TGameLobby>, IDisposable
        where TGameLobby : IGameLobby<TGameLobby>
    {
        private bool _disposed = false;
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly Dictionary<string, LobbyRegistration<TGameLobby>> _lobbies = [];
        private readonly Dictionary<Guid, LobbyRegistration<TGameLobby>> _userLobbyMap = [];

        public async ValueTask<Result> CloseLobbyAsync(Guid userId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = _lock.EnterWriteScope();
            if (!_userLobbyMap.TryGetValue(userId, out var registration))
                return Result.FromError(
                    new InvalidOperationException("User is not the host of a lobby."));

            if (!_lobbies.Remove(registration.Lobby.LobbyCode))
                return Result.FromError(
                    new InvalidOperationException("User had lobby registration but registration didn't exist."));

            return Result.Success;
        }

        public async ValueTask<Result<TGameLobby>> CreateLobbyAsync(Guid userId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var lobby = serviceProvider.GetService<TGameLobby>();
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

        public async ValueTask<Result<TGameLobby>> JoinLobbyAsync(string lobbyCode, UserRegistration registration)
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

        public void Dispose()
        {
            if (_disposed) return;

            _lock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
