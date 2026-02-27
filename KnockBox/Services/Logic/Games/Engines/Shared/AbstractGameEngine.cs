using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.Shared;

namespace KnockBox.Services.Logic.Games.Engines.Shared
{
    public abstract class AbstractGameEngine
    {
        /// <summary>
        /// The max player count for this type of game.
        /// </summary>
        public int MaxPlayerCount { get; }

        /// <summary>
        /// Creates a new initialized state read for players to join.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public abstract Task<Result<AbstractGameState>> CreateStateAsync(CancellationToken ct = default);

        /// <summary>
        /// Starts the game state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public abstract Task StartAsync(AbstractGameState state, CancellationToken ct = default);
    }
}
