using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Per-game context that holds shared data and helpers used by FSM states.
    /// Created when the game starts and stored on <see cref="DrawnToDressGameState"/>.
    /// </summary>
    public class DrawnToDressGameContext(DrawnToDressGameState state, ILogger logger)
    {
        public DrawnToDressGameState State { get; } = state;
        public ILogger Logger { get; } = logger;
    }
}
