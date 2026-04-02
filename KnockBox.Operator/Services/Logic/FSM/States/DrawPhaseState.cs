using System;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class DrawPhaseState : IOperatorGameState, ITimedGameState<OperatorGameContext, OperatorCommand>
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

        if (command is DrawCardsCommand draw)
        {
            var pState = context.GamePlayers[draw.PlayerId];
            int cardsNeeded = 5 - pState.Hand.Count;

            for (int i = 0; i < cardsNeeded; i++)
            {
                if (context.State.Deck.Count == 0) break;
                var card = context.State.Deck[0];
                context.State.Deck.RemoveAt(0);
                pState.Hand.Add(card);
            }

            if (context.State.Deck.Count == 0)
            {
                bool hasMoves = false;
                foreach (var p in context.GamePlayers.Values)
                {
                    if (p.Hand.Any(c => c.Type != CardType.Action || c.ActionValue != CardAction.Shield))
                    {
                        hasMoves = true;
                        break;
                    }
                }
                
                if (!hasMoves)
                {
                    context.State.Phase = OperatorGamePhase.GameOver;
                    return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new GameOverState());
                }
            }

            context.State.TurnManager.NextTurn();
            context.State.Phase = OperatorGamePhase.Play;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for DrawPhase.");
    }

    public ValueResult<TimeSpan> GetRemainingTime(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<TimeSpan>.FromValue(TimeSpan.MaxValue);
        var elapsed = now - context.State.StateStartTime;
        var remaining = context.State.Config.DrawPhaseTimeout - elapsed;
        return ValueResult<TimeSpan>.FromValue(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> Tick(OperatorGameContext context, DateTimeOffset now)
    {
        if (!context.State.Config.TimersEnabled) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);

        var elapsed = now - context.State.StateStartTime;
        if (elapsed >= context.State.Config.DrawPhaseTimeout)
        {
            var playerId = context.State.TurnManager.CurrentPlayer;
            if (playerId != null && context.GamePlayers.TryGetValue(playerId, out var pState))
            {
                int cardsNeeded = 5 - pState.Hand.Count;

                for (int i = 0; i < cardsNeeded; i++)
                {
                    if (context.State.Deck.Count == 0) break;
                    var card = context.State.Deck[0];
                    context.State.Deck.RemoveAt(0);
                    pState.Hand.Add(card);
                }
            }

            if (context.State.Deck.Count == 0)
            {
                bool hasMoves = false;
                foreach (var p in context.GamePlayers.Values)
                {
                    if (p.Hand.Any(c => c.Type != CardType.Action || c.ActionValue != CardAction.Shield))
                    {
                        hasMoves = true;
                        break;
                    }
                }
                
                if (!hasMoves)
                {
                    context.State.Phase = OperatorGamePhase.GameOver;
                    return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new GameOverState());
                }
            }

            context.State.TurnManager.NextTurn();
            context.State.Phase = OperatorGamePhase.Play;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }
}
