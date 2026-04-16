using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Codeword.Services.Logic.Games.FSM
{
    /// <summary>
    /// Type alias for the generic FSM state contract used by Consult the Card.
    /// States implement <see cref="IGameState{TContext,TCommand}"/> with
    /// <see cref="CodewordGameContext"/> and <see cref="CodewordCommand"/>.
    /// </summary>
    public interface ICodewordGameState
        : IGameState<CodewordGameContext, CodewordCommand>;

    /// <summary>
    /// Type alias for the generic timed FSM state contract used by Consult the Card.
    /// Timed states implement <see cref="ITimedGameState{TContext,TCommand}"/> with
    /// <see cref="CodewordGameContext"/> and <see cref="CodewordCommand"/>.
    /// </summary>
    public interface ITimedCodewordGameState
        : ITimedGameState<CodewordGameContext, CodewordCommand>, ICodewordGameState;
}
