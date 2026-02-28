using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.Navigation.Games.DiceSimulator;
using KnockBox.Services.Navigation.Games.CardCounter;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.Logic.Games.Shared
{
    public class LobbyService(
        IServiceProvider serviceProvider,
        ILobbyCodeService lobbyCodeService) : ILobbyService
    {
        private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbies = [];

        public async Task<Result> CloseLobbyAsync(
            User user, 
            LobbyRegistration registration, 
            CancellationToken ct = default)
        {
            if (user.Id != registration.State.Host.Id)
                return Result.FromError(
                    new InvalidOperationException($"User [{user.Name}] is not the host of the lobby and cannot close it."));

            if (!_lobbies.TryRemove(NormalizeLobbyCode(registration.Code), out _))
                return Result.FromError(
                    new KeyNotFoundException($"Unable to find lobby with code [{registration.Code}]."));

            var releaseResult = await lobbyCodeService.ReleaseLobbyCodeAsync(registration.Code, ct);
            if (releaseResult.TryGetError(out var error))
                return Result.FromError(error);

            return Result.Success;
        }

        public async Task<Result<LobbyRegistration>> CreateLobbyAsync(
            User host, 
            GameType gameType, 
            CancellationToken ct = default)
        {
            // TODO: Create implementations of these engines
            AbstractGameEngine? engine = gameType switch
            {
                GameType.SplitTheDeck => null,
                GameType.DiceSimulator => serviceProvider.GetService<DiceSimulatorGameEngine>(),
                GameType.CardCounter => serviceProvider.GetService<CardCounterGameEngine>(),
                _ => null
            };

            if (engine is null)
                return Result.FromError<LobbyRegistration>(new Exception($"Game engine for [{gameType}] not registered."));

            var stateResult = await engine.CreateStateAsync(host, ct);
            if (!stateResult.TryGetValue(out var gameState))
                return Result.FromError<LobbyRegistration>(stateResult.Error);

            var lobbyUriResult = CreateLobbyUri(gameType);
            if (!lobbyUriResult.TryGetValue(out var lobbyUri))
                return Result.FromError<LobbyRegistration>(lobbyUriResult.Error);

            var lobbyCodeResult = await lobbyCodeService.IssueLobbyCodeAsync(ct);
            if (!lobbyCodeResult.TryGetValue(out var lobbyCode)) // Service garauntees that lobby code is normalized
                return Result.FromError<LobbyRegistration>(lobbyCodeResult.Error);

            var lobbyRegistration = new LobbyRegistration(lobbyCode, lobbyUri, gameType, gameState);
            if (!_lobbies.TryAdd(lobbyCode, lobbyRegistration))
                return Result.FromError<LobbyRegistration>(new InvalidOperationException($"Game with lobby code [{lobbyCode}] already exists."));

            return Result.FromValue(lobbyRegistration);
        }

        public async Task<Result<UserRegistration>> JoinLobbyAsync(
            User user, 
            string lobbyCode, 
            CancellationToken ct = default)
        {
            if (!_lobbies.TryGetValue(NormalizeLobbyCode(lobbyCode), out var registration))
                return Result.FromError<UserRegistration>(new KeyNotFoundException($"Unable to find lobby with code [{lobbyCode}]."));

            Result<IDisposable> registrationResult = null!;
            var executionResult = registration.State.Execute(() =>
            {
                registrationResult = registration.State.RegisterPlayer(user);
            });

            if (executionResult.TryGetError(out var error))
                return Result.FromError<UserRegistration>(error);

            if (!registrationResult.TryGetValue(out var unsubscriber))
                return Result.FromError<UserRegistration>(registrationResult.Error);

            var userRegistration = new UserRegistration(user, unsubscriber, registration);
            return Result.FromValue(userRegistration);
        }

        private static string NormalizeLobbyCode(string lobbyCode)
        {
            return lobbyCode.Trim().ToUpperInvariant();
        }

        private static Result<string> CreateLobbyUri(GameType gameType)
        {
            if (!gameType.TryGetNavigationString(out var navigationString))
                return Result.FromError<string>(new ArgumentException($"Game type [{gameType}] does not have a defined navigation string attribute."));

            var guidA = Guid.NewGuid();
            var guidB = Guid.NewGuid();

            string lobbyId = $"{guidA}-{guidB}";

            return Result.FromValue($"room/{navigationString}/{lobbyId}");
        }
    }
}
