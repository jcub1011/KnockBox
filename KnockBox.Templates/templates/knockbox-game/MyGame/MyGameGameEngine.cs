// -----------------------------------------------------------------------------
// Stateless game engine.
//
// Registered as a singleton by MyGameModule.RegisterServices (via
// AddGameEngine<TEngine>). ONE instance exists for the entire host process
// regardless of how many rooms are active; every method takes the room's state
// as a parameter. Never cache per-room data on this class — put it on
// MyGameGameState instead.
//
// This class owns lifecycle hooks (CreateStateAsync, StartAsync) plus any
// game-specific commands you expose (e.g., PlaceBet, DrawCard). Each command
// should open a state.Execute(...) block, mutate the state inside it, and
// return a Result / ValueResult<T>.
// -----------------------------------------------------------------------------

using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace MyGame;

/// <summary>
/// The stateless game engine for this plugin. Subclasses
/// <see cref="AbstractGameEngine"/>; registered as a DI singleton.
/// </summary>
/// <remarks>
/// <para>
/// The two constructor arguments to <c>base(minPlayers, maxPlayers)</c> set the
/// player-count range for this game. The platform validates joins against these
/// bounds before letting a player register with the state.
/// </para>
/// <para>
/// The separate <c>stateLogger</c> parameter exists so that per-room log entries
/// carry the <c>MyGameGameState</c> category name. Pass it to the state's
/// constructor inside <see cref="CreateStateAsync"/>.
/// </para>
/// </remarks>
public class MyGameGameEngine(
    ILogger<MyGameGameEngine> logger,
    ILogger<MyGameGameState> stateLogger)
    // First ctor arg is the minimum player count; second is the maximum.
    // Tune these to your game. The platform enforces them during JoinLobby.
    : AbstractGameEngine(2, 8)
{
    /// <summary>
    /// Called by <c>LobbyService.CreateLobbyAsync</c> when the host clicks
    /// "Create Lobby". Returns a populated <see cref="MyGameGameState"/>, or a
    /// failure <see cref="ValueResult{T}"/> if the request is invalid.
    /// </summary>
    /// <param name="host">The player creating the lobby. Becomes the room's host.</param>
    /// <param name="ct">Cooperative cancellation from the request pipeline.</param>
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default)
    {
        var state = new MyGameGameState(host, stateLogger);

        // IsJoinable = true opens the lobby to additional players. Flip it to
        // false in StartAsync (or whenever the game's "locked in" phase begins).
        state.UpdateJoinableStatus(true);

        logger.LogInformation("Created game state for host [{HostId}].", host.Id);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    /// <summary>
    /// Called from the lobby page when the host clicks "Start Game". Validates
    /// the request, flips the lobby out of joinable mode, and initializes the
    /// first game-specific state.
    /// </summary>
    /// <remarks>
    /// Callers consume the returned <see cref="Result"/> via
    /// <c>TryGetFailure(out var error)</c> / <c>IsSuccess</c>. Prefer returning
    /// failures over throwing — the <c>Result</c> / <c>ValueResult&lt;T&gt;</c>
    /// pattern is the engine's control-flow vocabulary.
    /// </remarks>
    public override Task<Result> StartAsync(
        User host, AbstractGameState state, CancellationToken ct = default)
    {
        // Defensive type-check: the platform plumbs a base-typed state back to
        // us; cast to our concrete type before touching game-specific fields.
        if (state is not MyGameGameState gameState)
            return Task.FromResult(Result.FromError("Invalid state type.", "Internal error."));

        // Only the host can start the game. Non-host players clicking Start
        // would be a client-side bug; we still reject it server-side.
        if (host != gameState.Host)
            return Task.FromResult(Result.FromError("Only the host can start the game."));

        // All mutation inside Execute. The base class:
        //   - acquires the state's SemaphoreSlim(1,1),
        //   - runs this lambda,
        //   - releases the semaphore,
        //   - and *then* notifies StateChangedEventManager subscribers — outside
        //     the lock — so re-entrant Execute calls from handlers are safe.
        var executeResult = gameState.Execute(() =>
        {
            gameState.UpdateJoinableStatus(false);
            // TODO: Initialize your game state here (deal cards, pick first
            //       player, seed RNG, etc.).
        });

        return Task.FromResult(executeResult);
    }
}
