using System;
using System.Linq;
using System.Collections.Generic;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class PlayPhaseState : IOperatorGameState, ITimedGameState<OperatorGameContext, OperatorCommand>
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
        if (command.PlayerId != context.State.TurnManager.CurrentPlayer)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Not your turn.");

        if (command is SkipTurnCommand skip)
        {
            var pState = context.GamePlayers[skip.PlayerId];
            if (pState.Hand.All(c => c.Type == CardType.Action && c.ActionValue == CardAction.Shield))
            {
                context.State.Phase = OperatorGamePhase.Draw;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
            }
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Cannot skip unless hand is only Shields.");
        }

        if (command is PlayCardsCommand play)
        {
            if (play.CardIds.Count == 0)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Must play at least 1 card.");

            var pState = context.GamePlayers[play.PlayerId];
            var playedCards = new List<Card>();

            foreach (var id in play.CardIds)
            {
                var cardIdx = pState.Hand.FindIndex(c => c.Id == id);
                if (cardIdx == -1) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Card not found in hand.");
                playedCards.Add(pState.Hand[cardIdx]);
            }

            foreach (var c in playedCards)
            {
                pState.Hand.Remove(c);
                context.State.DiscardPile.Add(c);
            }

            bool hasTargetedAction = playedCards.Any(c => c.Type == CardType.Action && 
                (c.ActionValue == CardAction.Steal || c.ActionValue == CardAction.LiabilityTransfer || 
                 c.ActionValue == CardAction.HostileTakeover || c.ActionValue == CardAction.Audit));

            if (hasTargetedAction && !string.IsNullOrEmpty(play.TargetPlayerId))
            {
                context.State.PendingActionCommand = play;
                context.State.ReactionTargetPlayerId = play.TargetPlayerId;
                context.State.Phase = OperatorGamePhase.Reaction;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
            }

            var numbers = playedCards.Where(c => c.Type == CardType.Number).ToList();
            if (numbers.Any())
            {
                decimal val = 0;
                foreach (var num in numbers)
                {
                    val = val * 10 + num.NumberValue;
                }
                
                var (newScore, newOp) = OperatorGameContext.CalculateNewScore(pState.CurrentPoints, pState.ActiveOperator, val);
                pState.CurrentPoints = newScore;
                pState.ScoreTimestamp = DateTimeOffset.UtcNow;
            }

            var opCard = playedCards.LastOrDefault(c => c.Type == CardType.Operator);
            if (opCard.Type == CardType.Operator)
            {
                pState.ActiveOperator = opCard.OperatorValue;
            }

            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for PlayPhase.");
    }

    public ValueResult<TimeSpan> GetRemainingTime(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<TimeSpan>.FromValue(TimeSpan.MaxValue);
        var elapsed = now - context.State.StateStartTime;
        var remaining = context.State.Config.PlayPhaseTimeout - elapsed;
        return ValueResult<TimeSpan>.FromValue(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);

        var elapsed = now - context.State.StateStartTime;
        if (elapsed >= context.State.Config.PlayPhaseTimeout)
        {
            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }
}
