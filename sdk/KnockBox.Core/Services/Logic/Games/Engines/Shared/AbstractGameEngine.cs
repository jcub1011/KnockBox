using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Core.Services.Logic.Games.Engines.Shared
{
    public abstract class AbstractGameEngine
    {
        /// <summary>
        /// Initializes a new instance with default (zero) player count limits.
        /// </summary>
        protected AbstractGameEngine() { }

        /// <summary>
        /// Initializes a new instance with explicit player count limits.
        /// </summary>
        protected AbstractGameEngine(int minPlayerCount, int maxPlayerCount)
        {
            MinPlayerCount = minPlayerCount;
            MaxPlayerCount = maxPlayerCount;
        }

        /// <summary>
        /// The max player count for this type of game.
        /// </summary>
        public int MaxPlayerCount { get; }

        /// <summary>
        /// The minimum player count for this type of game.
        /// </summary>
        public int MinPlayerCount { get; }

        /// <summary>
        /// Creates a new initialized state ready for players to join.
        /// </summary>
        public abstract Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default);

        /// <summary>
        /// Starts the game. The base implementation validates that <paramref name="caller"/>
        /// is the lobby's host (<see cref="AbstractGameState.Host"/>), then delegates to
        /// <see cref="StartAsyncCore"/>. Plugins implement the game-specific start logic by
        /// overriding <see cref="StartAsyncCore"/>; host-identity authorization lives in the
        /// platform so every plugin inherits it automatically.
        /// </summary>
        public async Task<Result> StartAsync(User caller, AbstractGameState state, CancellationToken ct = default)
        {
            if (caller is null) return Result.FromError("Caller is required.");
            if (state is null) return Result.FromError("State is required.");
            if (caller.Id != state.Host.Id)
                return Result.FromError("Only the host can start the game.");
            return await StartAsyncCore(state, ct);
        }

        /// <summary>
        /// Game-specific start logic. Invoked by <see cref="StartAsync"/> after host-identity
        /// authorization has succeeded. Engines should assume the caller is authorized;
        /// <see cref="AbstractGameState.Host"/> remains available for any additional per-game
        /// checks.
        /// </summary>
        protected abstract Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default);

        /// <summary>
        /// Checks if the game state is good to start. Default composition is
        /// <see cref="HasValidPlayerCount"/> AND <see cref="IsLobbyOpen"/>; override
        /// and add game-specific readiness rules as needed.
        /// </summary>
        public virtual Task<bool> CanStartAsync(AbstractGameState state, CancellationToken ct = default)
        {
            return Task.FromResult(HasValidPlayerCount(state) && IsLobbyOpen(state));
        }

        /// <summary>
        /// Returns true when the player count is within
        /// [<see cref="MinPlayerCount"/>, <see cref="MaxPlayerCount"/>]. Does <b>not</b>
        /// check <see cref="AbstractGameState.IsJoinable"/>; compose with
        /// <see cref="IsLobbyOpen"/> when you need both.
        /// </summary>
        protected bool HasValidPlayerCount(AbstractGameState state)
        {
            var count = state.Players.Length;
            return MinPlayerCount <= count && count <= MaxPlayerCount;
        }

        /// <summary>
        /// Returns true when the lobby is currently accepting joins
        /// (<see cref="AbstractGameState.IsJoinable"/>).
        /// </summary>
        protected bool IsLobbyOpen(AbstractGameState state) => state.IsJoinable;
    }

    /// <summary>
    /// Strongly-typed base for game engines whose per-room state is
    /// <typeparamref name="TState"/>. Resolves the concrete state from the
    /// untyped <see cref="AbstractGameState"/> exactly once, so concrete engines
    /// override <see cref="StartAsyncCore(TState, CancellationToken)"/> and never
    /// perform the cast themselves.
    /// </summary>
    /// <typeparam name="TState">The engine's concrete <see cref="AbstractGameState"/> subtype.</typeparam>
    public abstract class AbstractGameEngine<TState> : AbstractGameEngine
        where TState : AbstractGameState
    {
        /// <summary>
        /// Initializes a new instance with default (zero) player count limits.
        /// </summary>
        protected AbstractGameEngine() { }

        /// <summary>
        /// Initializes a new instance with explicit player count limits.
        /// </summary>
        protected AbstractGameEngine(int minPlayerCount, int maxPlayerCount)
            : base(minPlayerCount, maxPlayerCount) { }

        /// <summary>
        /// Casts the lobby's state to <typeparamref name="TState"/> and delegates to the
        /// typed <see cref="StartAsyncCore(TState, CancellationToken)"/>. Sealed so concrete
        /// engines cannot re-introduce an untyped override; a failed cast (which should never
        /// happen, since the engine that created the state also starts it) returns a populated
        /// error describing the actual-vs-expected type.
        /// </summary>
        protected sealed override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not TState typed)
                return Task.FromResult(Result.FromError(
                    "Error starting game.",
                    $"Game state of type [{state?.GetType().Name ?? "null"}] couldn't be cast to type [{typeof(TState).Name}]."));

            return StartAsyncCore(typed, ct);
        }

        /// <summary>
        /// Game-specific start logic with the engine's concrete state already resolved.
        /// Invoked by <see cref="AbstractGameEngine.StartAsync"/> after host-identity
        /// authorization has succeeded.
        /// </summary>
        protected abstract Task<Result> StartAsyncCore(TState state, CancellationToken ct = default);

        /// <summary>
        /// Returns a finished game to its lobby so players can join/leave and settings can be
        /// changed. Like <see cref="AbstractGameEngine.StartAsync"/>, the authorization and locking
        /// discipline live in the platform so every plugin inherits them automatically: this method
        /// validates the caller is the host and the game is in a terminal phase, then runs the
        /// game-specific reset inside <see cref="AbstractGameState.Execute(Action)"/> and re-opens the
        /// lobby. Flipping the state back to joinable re-renders every player's page at the lobby —
        /// no navigation needed. Plugins customize behavior by overriding <see cref="IsTerminalPhase"/>
        /// and <see cref="ResetForLobby"/>; games without a return-to-lobby flow leave the defaults.
        /// </summary>
        public Result ReturnToLobby(User caller, AbstractGameState state)
        {
            if (caller is null) return Result.FromError("Caller is required.");
            if (state is null) return Result.FromError("State is required.");
            if (state is not TState typed)
                return Result.FromError(
                    "Error returning to lobby.",
                    $"Game state of type [{state.GetType().Name}] couldn't be cast to type [{typeof(TState).Name}].");
            if (caller.Id != typed.Host.Id)
                return Result.FromError("Only the host can return the game to the lobby.");
            if (!IsTerminalPhase(typed))
                return Result.FromError("Can only return to the lobby after the game is over.");

            return typed.Execute(() =>
            {
                ResetForLobby(typed);
                typed.SetJoinable(true);
            });
        }

        /// <summary>
        /// Returns true when <paramref name="state"/> is in a phase from which
        /// <see cref="ReturnToLobby"/> is allowed (i.e. the game is over). Defaults to
        /// <c>false</c> so games that have no return-to-lobby flow reject the call; override
        /// to recognize the game's terminal phase.
        /// </summary>
        protected virtual bool IsTerminalPhase(TState state) => false;

        /// <summary>
        /// Clears the per-match state and sets the lobby phase. Invoked by
        /// <see cref="ReturnToLobby"/> inside the state's execute lock; the base method re-opens
        /// the lobby afterward, so overrides must <b>not</b> call
        /// <see cref="AbstractGameState.SetJoinable(bool)"/> themselves. Settings should be
        /// preserved. The default is a no-op (paired with the default
        /// <see cref="IsTerminalPhase"/> that rejects the call).
        /// </summary>
        protected virtual void ResetForLobby(TState state) { }
    }
}
