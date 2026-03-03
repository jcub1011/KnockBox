using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.Engines.Shared
{
    public abstract class AbstractGameEngine
    {
        /// <summary>
        /// The max player count for this type of game.
        /// </summary>
        public int MaxPlayerCount { get; }

        /// <summary>
        /// The minimum player count for this type of game.
        /// </summary>
        public int MinPlayerCount { get; }

        /// <summary>
        /// Creates a new initialized state read for players to join.
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
