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
        /// <param name="host"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public abstract Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default);

        /// <summary>
        /// Starts the game state. Only the host can start the game.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public abstract Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default);

        /// <summary>
        /// Checks if the game state is good to start.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public virtual Task<bool> CanStartAsync(AbstractGameState state)
        {
            return Task.FromResult(MinPlayerCount <= state.Players.Count
                && state.Players.Count <= MaxPlayerCount
                && state.IsJoinable);
        }
    }
}
