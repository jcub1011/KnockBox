using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;
using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Logic.Games.DiceSimulator;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.ConsultTheCard;

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
                return Result.FromError("You cannot close a lobby you aren't the host of.", $"User [{user.Name}] is not the host of the lobby and cannot close it.");

            if (!_lobbies.TryRemove(NormalizeLobbyCode(registration.Code), out _))
                return Result.FromError($"Lobby with code [{registration.Code}] not found.");

            var releaseResult = await lobbyCodeService.ReleaseLobbyCodeAsync(registration.Code, ct);
            if (releaseResult.IsCanceled)
                return Result.FromCancellation();

            if (releaseResult.TryGetFailure(out var error))
                return error;

            return Result.Success;
        }

        public async Task<ValueResult<LobbyRegistration>> CreateLobbyAsync(
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
                GameType.DrawnToDress => serviceProvider.GetService<DrawnToDressGameEngine>(),
                GameType.ConsultTheCard => serviceProvider.GetService<ConsultTheCardGameEngine>(),
                _ => null
            };

            if (engine is null)
                return ValueResult<LobbyRegistration>.FromError($"No game engine is registered for [{gameType}].");

            try
            {
                var stateResult = await engine.CreateStateAsync(host, ct);
                if (stateResult.IsCanceled) return ValueResult<LobbyRegistration>.FromCancellation();
                if (!stateResult.TryGetSuccess(out var gameState))
                    return ValueResult<LobbyRegistration>.FromError(stateResult.Error.Error);

                var lobbyUriResult = CreateLobbyUri(gameType);
                if (!lobbyUriResult.TryGetSuccess(out var lobbyUri))
                    return ValueResult<LobbyRegistration>.FromError(lobbyUriResult.Error.Error);

                var lobbyCodeResult = await lobbyCodeService.IssueLobbyCodeAsync(ct);
                if (!lobbyCodeResult.TryGetSuccess(out var lobbyCode)) // Service garauntees that lobby code is normalized
                    return ValueResult<LobbyRegistration>.FromError(lobbyCodeResult.Error.Error);

                var lobbyRegistration = new LobbyRegistration(lobbyCode, lobbyUri, gameType, gameState);
                if (!_lobbies.TryAdd(lobbyCode, lobbyRegistration))
                    return ValueResult<LobbyRegistration>.FromError($"Game with lobby code [{lobbyCode}] already exists.");

                return lobbyRegistration;
            }
            catch (OperationCanceledException) { return ValueResult<LobbyRegistration>.FromCancellation(); }
            catch (Exception ex) 
            {
                return ValueResult<LobbyRegistration>.FromError("Error creating lobby.", $"Exception occured while creating lobby: {ex}");
            }
        }

        public async Task<ValueResult<UserRegistration>> JoinLobbyAsync(
            User user, 
            string lobbyCode, 
            CancellationToken ct = default)
        {
            if (!_lobbies.TryGetValue(NormalizeLobbyCode(lobbyCode), out var registration))
                return ValueResult<UserRegistration>.FromError($"Lobby with code [{lobbyCode}] not found.");

            ValueResult<IDisposable> registrationResult = null!;
            var executionResult = registration.State.Execute(() =>
            {
                registrationResult = registration.State.RegisterPlayer(user);
            });

            if (executionResult.TryGetFailure(out var error))
                return ValueResult<UserRegistration>.FromError(error);

            if (!registrationResult.TryGetSuccess(out var unsubscriber))
                return ValueResult<UserRegistration>.FromError(registrationResult.Error.Error);

            return new UserRegistration(user, unsubscriber, registration);
        }

        private static string NormalizeLobbyCode(string lobbyCode)
        {
            return lobbyCode.Trim().ToUpperInvariant();
        }

        private static ValueResult<string> CreateLobbyUri(GameType gameType)
        {
            if (!gameType.TryGetNavigationString(out var navigationString))
                return ValueResult<string>.FromError("Failed to generate a uri for the lobby.", $"Game type [{gameType}] does not have a defined navigation string attribute.");

            var guidA = Guid.NewGuid();
            var guidB = Guid.NewGuid();

            string lobbyId = $"{guidA}-{guidB}";

            return $"room/{navigationString}/{lobbyId}";
        }
    }
}
