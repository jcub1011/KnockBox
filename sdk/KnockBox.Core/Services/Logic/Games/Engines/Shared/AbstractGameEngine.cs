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
    }
}
