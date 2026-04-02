using System;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class ReactionState : IOperatorGameState, ITimedGameState<OperatorGameContext, OperatorCommand>
{
    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> OnEnter(OperatorGameContext context)
    {
        context.State.StateStartTime = DateTimeOffset.UtcNow;
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }

    public Result OnExit(OperatorGameContext context)
    {
        return Result.Success;
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> HandleCommand(OperatorGameContext context, OperatorCommand command)
    {
        if (command.PlayerId != context.State.ReactionTargetPlayerId)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Not your reaction.");

        if (command is PlayReactionCommand react)
        {
            var pState = context.GamePlayers[react.PlayerId];
            var shieldIdx = pState.Hand.FindIndex(c => c.Id == react.ShieldCardId && c.Type == CardType.Action && c.ActionValue == CardAction.Shield);
            
            if (shieldIdx == -1) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Shield not found.");
            
            var shield = pState.Hand[shieldIdx];
            pState.Hand.RemoveAt(shieldIdx);
            context.State.DiscardPile.Add(shield);
            
            context.State.PendingActionCommand = null;
            context.State.ReactionTargetPlayerId = null;

            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }
        else if (command is PassReactionCommand)
        {
            context.State.PendingActionCommand = null;
            context.State.ReactionTargetPlayerId = null;
            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for ReactionPhase.");
    }

    public ValueResult<TimeSpan> GetRemainingTime(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<TimeSpan>.FromValue(TimeSpan.MaxValue);
        var elapsed = now - context.State.StateStartTime;
        var remaining = context.State.Config.ReactionPhaseTimeout - elapsed;
        return ValueResult<TimeSpan>.FromValue(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);

        var elapsed = now - context.State.StateStartTime;
        if (elapsed >= context.State.Config.ReactionPhaseTimeout)
        {
            context.State.PendingActionCommand = null;
            context.State.ReactionTargetPlayerId = null;
            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }
}
