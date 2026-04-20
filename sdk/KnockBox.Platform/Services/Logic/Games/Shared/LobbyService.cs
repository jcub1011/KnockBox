using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using KnockBox.Core.Plugins;
using KnockBox.Platform.Games;

namespace KnockBox.Services.Logic.Games.Shared
{
    internal sealed class LobbyService : ILobbyService
    {
        private readonly ILobbyCodeService _lobbyCodeService;
        private readonly IGameAvailabilityService _gameAvailability;
        private readonly ILogger<LobbyService> _logger;
        private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbies = [];
        private readonly Dictionary<string, GameRegistration> _gamesByRoute;

        public LobbyService(
            IServiceProvider serviceProvider,
            ILobbyCodeService lobbyCodeService,
            IGameAvailabilityService gameAvailability,
            IEnumerable<IGameModule> gameModules,
            ILogger<LobbyService> logger)
        {
            _lobbyCodeService = lobbyCodeService;
            _gameAvailability = gameAvailability;
            _logger = logger;
            _gamesByRoute = new Dictionary<string, GameRegistration>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in gameModules)
            {
                var engine = serviceProvider.GetKeyedService<AbstractGameEngine>(module.RouteIdentifier);
                if (engine is null)
                {
                    _logger.LogError(
                        "Game module [{Name}] with route identifier [{RouteIdentifier}] did not register an AbstractGameEngine; it will be unavailable.",
                        module.Name,
                        module.RouteIdentifier);
                    continue;
                }

                _gamesByRoute[module.RouteIdentifier] = new GameRegistration(module, engine);
            }
        }

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

            var releaseResult = await _lobbyCodeService.ReleaseLobbyCodeAsync(registration.Code, ct);
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
            if (string.IsNullOrWhiteSpace(routeIdentifier) || !_gamesByRoute.TryGetValue(routeIdentifier, out var game))
                return ValueResult<LobbyRegistration>.FromError($"No game registered for route identifier [{routeIdentifier}].");

            if (!_gameAvailability.IsEnabled(routeIdentifier))
                return ValueResult<LobbyRegistration>.FromError(
                    "This game is currently disabled.",
                    $"Lobby creation rejected: game [{routeIdentifier}] is disabled via admin.");

            AbstractGameState? gameState = null;
            try
            {
                var stateResult = await game.Engine.CreateStateAsync(host, ct);
                if (stateResult.IsCanceled) return ValueResult<LobbyRegistration>.FromCancellation();
                if (!stateResult.TryGetSuccess(out var state))
                    return ValueResult<LobbyRegistration>.FromError(stateResult.Error.Error);

                gameState = state;

                var lobbyUriResult = CreateLobbyUri(routeIdentifier);
                if (!lobbyUriResult.TryGetSuccess(out var lobbyUri))
                {
                    gameState.Dispose();
                    return ValueResult<LobbyRegistration>.FromError(lobbyUriResult.Error.Error);
                }

                var lobbyCodeResult = await _lobbyCodeService.IssueLobbyCodeAsync(ct);
                if (!lobbyCodeResult.TryGetSuccess(out var lobbyCode)) // Service guarantees that lobby code is normalized
                {
                    gameState.Dispose();
                    return ValueResult<LobbyRegistration>.FromError(lobbyCodeResult.Error.Error);
                }

                var lobbyRegistration = new LobbyRegistration(lobbyCode, lobbyUri, game.Module.Name, routeIdentifier, gameState);
                if (!_lobbies.TryAdd(lobbyCode, lobbyRegistration))
                {
                    // This branch means LobbyCodeService handed us a code that is
                    // already in _lobbies -- a broken invariant. Release the code
                    // back to the issuer on a best-effort basis so we don't leak
                    // it permanently; log loudly because reaching here is a bug.
                    gameState.Dispose();
                    var releaseResult = await _lobbyCodeService.ReleaseLobbyCodeAsync(lobbyCode, ct);
                    if (releaseResult.TryGetFailure(out var releaseError))
                    {
                        _logger.LogError(
                            "Failed to release lobby code [{LobbyCode}] after a TryAdd collision in CreateLobbyAsync: {Error}",
                            lobbyCode,
                            releaseError.InternalMessage);
                    }
                    return ValueResult<LobbyRegistration>.FromError($"Game with lobby code [{lobbyCode}] already exists.");
                }

                gameState = null; // Successfully added to _lobbies; ownership transferred.
                return lobbyRegistration;
            }
            catch (OperationCanceledException)
            {
                gameState?.Dispose();
                return ValueResult<LobbyRegistration>.FromCancellation();
            }
            catch (Exception ex)
            {
                gameState?.Dispose();
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

        public IReadOnlyDictionary<string, int> GetLobbyCountsByRoute()
        {
            // ConcurrentDictionary.Values is a snapshot; safe to enumerate
            // from any thread without additional locking.
            return _lobbies.Values
                .GroupBy(r => r.RouteIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeLobbyCode(string lobbyCode)
        {
            return lobbyCode.Trim().ToUpperInvariant();
        }

        private ValueResult<string> CreateLobbyUri(string routeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(routeIdentifier) || !_gamesByRoute.ContainsKey(routeIdentifier))
                return ValueResult<string>.FromError("Failed to generate a uri for the lobby.", $"Unknown game route identifier [{routeIdentifier}].");

            var guidA = Guid.NewGuid();
            var guidB = Guid.NewGuid();

            string lobbyId = $"{guidA}-{guidB}";

            return $"room/{routeIdentifier}/{lobbyId}";
        }

        private readonly record struct GameRegistration(IGameModule Module, AbstractGameEngine Engine);
    }
}
