using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation.Games;

namespace KnockBox.Services.State.Games.Lobbies
{
    internal record class HostedLobbyRegistration(UserRegistration Host, LobbyRegistration Registration) : IDisposable
    {
        public void Dispose() => Registration.Unsubscriber.Dispose();
    }

    public class LobbyManager(ILobbyCodeService lobbyCodeService, ILobbyUriProvider lobbyUriProvider, ILogger<LobbyManager> logger) : ILobbyManager
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, HostedLobbyRegistration> _lobbies = [];

        public async ValueTask<Result<string>> CreateLobbyAsync(UserRegistration user, GameType gameType, CancellationToken ct = default)
        {
            var lobbyCodeResult = await lobbyCodeService.IssueLobbyCodeAsync(ct);
            if (!lobbyCodeResult.TryGetValue(out var lobbyCode))
                return Result.FromError<string>(lobbyCodeResult.Error);

            var lobbyRegistrationResult = lobbyUriProvider.RegisterLobby(lobbyCode, gameType);
            if (!lobbyRegistrationResult.TryGetValue(out var registration))
                return Result.FromError<string>(lobbyRegistrationResult.Error);

            lock (_lock)
            {
                var hostedRegistration = new HostedLobbyRegistration(user, registration);
                if (!_lobbies.TryAdd(NormalizeLobbyCode(lobbyCode), hostedRegistration))
                {
                    hostedRegistration.Dispose();
                    logger.LogError("Creating a lobby resulted in a duplicate lobby code [{lobbyCode}], which should not be possible.", lobbyCode);
                    return Result.FromError<string>(new InvalidOperationException($"Error creating lobby."));
                }
            }

            return Result.FromValue(registration.Uri);
        }

        public async ValueTask<Result<string>> JoinLobbyAsync(UserRegistration user, string lobbyCode, CancellationToken ct = default)
        {
            lock (_lock)
            {
                return _lobbies.TryGetValue(NormalizeLobbyCode(lobbyCode), out var hostedRegistration)
                    ? Result.FromValue(hostedRegistration.Registration.Uri)
                    : Result.FromError<string>(new KeyNotFoundException($"Unable to find lobby with code [{lobbyCode}]."));
            }
        }

        public ValueTask<Result> StartLobbyAsync(UserRegistration user, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        private static string NormalizeLobbyCode(string lobbyCode)
        {
            return lobbyCode.Trim().ToUpperInvariant();
        }
    }
}
