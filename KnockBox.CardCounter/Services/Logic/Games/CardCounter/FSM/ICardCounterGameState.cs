using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM
{
    /// <summary>
    /// Type alias for the generic FSM state contract used by Card Counter.
    /// States implement <see cref="IGameState{TContext,TCommand}"/> with
    /// <see cref="CardCounterGameContext"/> and <see cref="CardCounterCommand"/>.
    /// </summary>
    public interface ICardCounterGameState
        : IGameState<CardCounterGameContext, CardCounterCommand>;

    /// <summary>
    /// Type alias for the generic timed FSM state contract used by Card Counter.
    /// Timed states implement <see cref="ITimedGameState{TContext,TCommand}"/> with
    /// <see cref="CardCounterGameContext"/> and <see cref="CardCounterCommand"/>.
    /// </summary>
    public interface ITimedCardCounterGameState
        : ITimedGameState<CardCounterGameContext, CardCounterCommand>, ICardCounterGameState;
}
