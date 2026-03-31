using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM
{
    /// <summary>
    /// Type alias for the generic FSM state contract used by Consult the Card.
    /// States implement <see cref="IGameState{TContext,TCommand}"/> with
    /// <see cref="ConsultTheCardGameContext"/> and <see cref="ConsultTheCardCommand"/>.
    /// </summary>
    public interface IConsultTheCardGameState
        : IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>;

    /// <summary>
    /// Type alias for the generic timed FSM state contract used by Consult the Card.
    /// Timed states implement <see cref="ITimedGameState{TContext,TCommand}"/> with
    /// <see cref="ConsultTheCardGameContext"/> and <see cref="ConsultTheCardCommand"/>.
    /// </summary>
    public interface ITimedConsultTheCardGameState
        : ITimedGameState<ConsultTheCardGameContext, ConsultTheCardCommand>, IConsultTheCardGameState;
}
