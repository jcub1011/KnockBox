using System;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class GameOverState : IOperatorGameState
{
    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> OnEnter(OperatorGameContext context)
    {
        var winner = context.GamePlayers.Values
            .OrderBy(p => Math.Abs(p.CurrentPoints))
            .ThenBy(p => p.ScoreTimestamp)
            .FirstOrDefault();

        context.State.WinnerPlayerId = winner?.UserId;

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }

    public Result OnExit(OperatorGameContext context)
    {
        return Result.Success;
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> HandleCommand(OperatorGameContext context, OperatorCommand command)
    {
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Game is over. No more commands accepted.");
    }
}
