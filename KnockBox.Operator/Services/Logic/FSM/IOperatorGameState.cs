using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Operator.Services.Logic.FSM;

public interface IOperatorGameState
    : IGameState<OperatorGameContext, OperatorCommand>;
