using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Type alias for the generic FSM state contract used by Drawn to Dress.
    /// States implement <see cref="IGameState{TContext,TCommand}"/> with
    /// <see cref="DrawnToDressGameContext"/> and <see cref="DrawnToDressCommand"/>.
    /// </summary>
    public interface IDrawnToDressGameState
        : IGameState<DrawnToDressGameContext, DrawnToDressCommand>;
}
