using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Type alias for the generic FSM state contract used by Drawn To Dress.
    /// States implement <see cref="IGameState{TContext,TCommand}"/> with
    /// <see cref="DrawnToDressGameContext"/> and <see cref="DrawnToDressCommand"/>.
    /// </summary>
    public interface IDrawnToDressGameState
        : IGameState<DrawnToDressGameContext, DrawnToDressCommand>;

    /// <summary>
    /// Type alias for the generic timed FSM state contract used by Drawn To Dress.
    /// Timed states implement <see cref="ITimedGameState{TContext,TCommand}"/> with
    /// <see cref="DrawnToDressGameContext"/> and <see cref="DrawnToDressCommand"/>,
    /// making timer-driven behaviour easy to add in later issues.
    /// </summary>
    public interface ITimedDrawnToDressGameState
        : ITimedGameState<DrawnToDressGameContext, DrawnToDressCommand>, IDrawnToDressGameState
    {
        /// <summary>
        /// When <see langword="true"/>, this state respects the <see cref="DrawnToDressConfig.EnableTimer"/>
        /// configuration and will not auto-advance if it is disabled.
        /// Default is <see langword="false"/> (always auto-advance).
        /// </summary>
        bool IsTimerOptional => false;
    }
}
