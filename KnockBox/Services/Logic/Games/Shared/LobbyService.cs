using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using KnockBox.Core.Plugins;

namespace KnockBox.Services.Logic.Games.Shared
{
    public class LobbyService(
        IServiceProvider serviceProvider,
        ILobbyCodeService lobbyCodeService,
        IEnumerable<IGameModule> gameModules) : ILobbyService
    {
        private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbies = [];

        public async Task<Result> CloseLobbyAsync(
            User user, 
            LobbyRegistration registration, 
            CancellationToken ct = default)
        {
            if (user.Id != registration.State.Host.Id)
                return Result.FromError("You cannot close a lobby you aren't the host of.", $"User [{user.Name}] is not the host of the lobby and cannot close it.");

            if (!_lobbies.TryRemove(NormalizeLobbyCode(registration.Code), out var removed))
                return Result.FromError($"Lobby with code [{registration.Code}] not found.");

            removed.State.Dispose();

            var releaseResult = await lobbyCodeService.ReleaseLobbyCodeAsync(registration.Code, ct);
            if (releaseResult.IsCanceled)
                return Result.FromCancellation();

            if (releaseResult.TryGetFailure(out var error))
                return error;

            return Result.Success;
        }

        public async Task<ValueResult<LobbyRegistration>> CreateLobbyAsync(
            User host, 
            string routeIdentifier, 
            CancellationToken ct = default)
        {
            AbstractGameEngine? engine = serviceProvider.GetKeyedService<AbstractGameEngine>(routeIdentifier);

            if (engine is null)
                return ValueResult<LobbyRegistration>.FromError($"No game engine is registered for [{routeIdentifier}].");

            try
            {
                var stateResult = await engine.CreateStateAsync(host, ct);
                if (stateResult.IsCanceled) return ValueResult<LobbyRegistration>.FromCancellation();
                if (!stateResult.TryGetSuccess(out var gameState))
                    return ValueResult<LobbyRegistration>.FromError(stateResult.Error.Error);

                var lobbyUriResult = CreateLobbyUri(routeIdentifier);
                if (!lobbyUriResult.TryGetSuccess(out var lobbyUri))
                    return ValueResult<LobbyRegistration>.FromError(lobbyUriResult.Error.Error);

                var lobbyCodeResult = await lobbyCodeService.IssueLobbyCodeAsync(ct);
                if (!lobbyCodeResult.TryGetSuccess(out var lobbyCode)) // Service garauntees that lobby code is normalized
                    return ValueResult<LobbyRegistration>.FromError(lobbyCodeResult.Error.Error);

                var module = gameModules.FirstOrDefault(m => m.RouteIdentifier == routeIdentifier);
                if (module is null)
                    return ValueResult<LobbyRegistration>.FromError($"Unknown game route identifier [{routeIdentifier}].");

                var lobbyRegistration = new LobbyRegistration(lobbyCode, lobbyUri, module.Name, routeIdentifier, gameState);
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

        private static ValueResult<string> CreateLobbyUri(string routeIdentifier)
        {
            var guidA = Guid.NewGuid();
            var guidB = Guid.NewGuid();

            string lobbyId = $"{guidA}-{guidB}";

            return $"room/{routeIdentifier}/{lobbyId}";
        }
    }
}
