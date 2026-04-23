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
        /// Starts the game state. Caller-identity verification (e.g. "only the host may start")
        /// is the responsibility of the invoker — engines should assume the caller is authorized.
        /// The host is available via <see cref="AbstractGameState.Host"/> if the engine needs it.
        /// </summary>
        public abstract Task<Result> StartAsync(AbstractGameState state, CancellationToken ct = default);

        /// <summary>
        /// Checks if the game state is good to start.
        /// </summary>
        public virtual Task<bool> CanStartAsync(AbstractGameState state, CancellationToken ct = default)
        {
            return Task.FromResult(HasValidPlayerCount(state));
        }

        /// <summary>
        /// Returns true when the state has a player count inside
        /// [<see cref="MinPlayerCount"/>, <see cref="MaxPlayerCount"/>] and the lobby is still joinable.
        /// Override <see cref="CanStartAsync"/> and compose with this helper to add game-specific rules.
        /// </summary>
        protected bool HasValidPlayerCount(AbstractGameState state)
            => MinPlayerCount <= state.Players.Count
            && state.Players.Count <= MaxPlayerCount
            && state.IsJoinable;
    }
}
