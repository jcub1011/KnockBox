using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KnockBox.Core.Plugins;
using KnockBox.Platform.Games;
using KnockBox.Platform.Hubs;

namespace KnockBox.Services.Logic.Games.Shared
{
    internal sealed class LobbyService : ILobbyService, IHostedService
    {
        private readonly ILobbyCodeService _lobbyCodeService;
        private readonly IGameAvailabilityService _gameAvailability;
        private readonly GameViewCoordinator _viewCoordinator;
        private readonly ILogger<LobbyService> _logger;
        private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbies = [];
        // Secondary index keyed by the full lobby URI (`room/{routeIdentifier}/{guidA}-{guidB}`)
        // so the plugin HTTP dispatcher can resolve a room from a request path.
        // Mutated in lockstep with `_lobbies` (TryAdd on create; TryRemove on close + shutdown).
        private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbiesByUri = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameRegistration> _gamesByRoute;
        private int _shuttingDown;

        public LobbyService(
            IServiceProvider serviceProvider,
            ILobbyCodeService lobbyCodeService,
            IGameAvailabilityService gameAvailability,
            IEnumerable<IGameModule> gameModules,
            GameViewCoordinator viewCoordinator,
            ILogger<LobbyService> logger)
        {
            _lobbyCodeService = lobbyCodeService;
            _gameAvailability = gameAvailability;
            _viewCoordinator = viewCoordinator;
            _logger = logger;
            _gamesByRoute = new(StringComparer.OrdinalIgnoreCase);

            foreach (var module in gameModules)
            {
                var engine = serviceProvider.GetKeyedService<AbstractGameEngine>(module.Manifest.RouteIdentifier);
                if (engine is null)
                {
                    _logger.LogError(
                        "Game module [{Name}] with route identifier [{RouteIdentifier}] did not register an AbstractGameEngine; it will be unavailable.",
                        module.Manifest.Name,
                        module.Manifest.RouteIdentifier);
                    continue;
                }

                _gamesByRoute[module.Manifest.RouteIdentifier] = new GameRegistration(module, engine);
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

            _lobbiesByUri.TryRemove(removed.Uri, out _);

            // Tell any connected players the lobby is closing so they leave — while
            // they are still in the SignalR group and the state is still alive.
            await _viewCoordinator.NotifyLobbyClosedAsync(removed.Uri, removed.RouteIdentifier);

            // Tear down the per-lobby projection subscriber. Idempotent and also
            // triggered by the state-disposed callback below, but done explicitly
            // here so the subscription is gone the moment the lobby closes.
            _viewCoordinator.RemoveSubscription(removed.Uri);

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
            if (Volatile.Read(ref _shuttingDown) == 1)
                return ValueResult<LobbyRegistration>.FromError("Host is shutting down; no new lobbies can be created.");

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
                {
                    _logger.LogWarning(
                        "Game [{RouteIdentifier}] failed to create state: {Error}",
                        routeIdentifier,
                        stateResult.Error.Error.InternalMessage);
                    return ValueResult<LobbyRegistration>.FromError(stateResult.Error.Error);
                }

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

                var lobbyRegistration = new LobbyRegistration(lobbyCode, lobbyUri, game.Module.Manifest.Name, routeIdentifier, gameState);
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

                // Mirror into the URI index. The URI is built from the route id
                // and a fresh GUID pair, so collision is not a real concern; if
                // it ever fires we'd see it as a missed dispatcher lookup.
                _lobbiesByUri.TryAdd(lobbyUri, lobbyRegistration);

                // Install the single per-lobby projection subscriber for the
                // lobby's whole lifetime — not lazily on first hub join — so an
                // open lobby with no connected players still projects on the next
                // join. Idempotent; torn down in CloseLobbyAsync.
                _viewCoordinator.EnsureSubscribed(lobbyRegistration);

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
                _logger.LogError(ex, "Unexpected exception creating lobby for route [{RouteIdentifier}].", routeIdentifier);
                return ValueResult<LobbyRegistration>.FromError("Error creating lobby.", $"Exception occurred while creating lobby: {ex}");
            }
        }

        public Task<ValueResult<UserRegistration>> JoinLobbyAsync(
            User user,
            string lobbyCode,
            CancellationToken ct = default)
        {
            if (!_lobbies.TryGetValue(NormalizeLobbyCode(lobbyCode), out var registration))
                return Task.FromResult(ValueResult<UserRegistration>.FromError($"Lobby with code [{lobbyCode}] not found."));

            // RegisterPlayer wraps its own gate check + dictionary mutation in Execute;
            // wrapping here would deadlock on the non-reentrant execute lock.
            var registrationResult = registration.State.RegisterPlayer(user);
            if (!registrationResult.TryGetSuccess(out var unsubscriber))
                return Task.FromResult(ValueResult<UserRegistration>.FromError(registrationResult.Error.Error));

            return Task.FromResult<ValueResult<UserRegistration>>(new UserRegistration(user, unsubscriber, registration));
        }

        public bool TryGetByUri(string uri, [NotNullWhen(true)] out LobbyRegistration? registration)
        {
            if (string.IsNullOrEmpty(uri))
            {
                registration = null;
                return false;
            }

            return _lobbiesByUri.TryGetValue(uri, out registration);
        }

        public IReadOnlyDictionary<string, int> GetLobbyCountsByRoute()
        {
            // ConcurrentDictionary.Values is a snapshot; safe to enumerate
            // from any thread without additional locking. Counted in a single
            // pass into one dictionary — no GroupBy iterator/grouping allocations.
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in _lobbies.Values)
            {
                counts.TryGetValue(registration.RouteIdentifier, out var current);
                counts[registration.RouteIdentifier] = current + 1;
            }
            return counts;
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

        // ── IHostedService: graceful shutdown ────────────────────────────────
        //
        // Snapshots every open lobby on application stop and disposes its state so
        // subscribers (engine background tasks, scheduled callbacks, Blazor circuit
        // handlers) get a deterministic OnStateDisposed notification instead of being
        // torn down mid-flight by the runtime.

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Flag set *before* the snapshot so any CreateLobbyAsync racing
            // against shutdown rejects rather than leaking a lobby whose state
            // never gets disposed.
            Interlocked.Exchange(ref _shuttingDown, 1);

            // Snapshot first so we don't mutate the collection while enumerating.
            var snapshot = _lobbies.ToArray();
            _lobbies.Clear();
            _lobbiesByUri.Clear();

            foreach (var kvp in snapshot)
            {
                try
                {
                    kvp.Value.State.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error disposing lobby [{Code}] during shutdown.",
                        kvp.Key);
                }
            }

            return Task.CompletedTask;
        }
    }
}
