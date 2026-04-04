using System;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class DrawPhaseState : IOperatorGameState
{
    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> OnEnter(OperatorGameContext context)
    {
        context.State.Phase = OperatorGamePhase.Draw;

        // Auto-draw for current player: up to 3 cards, max hand size 5
        var playerId = context.State.TurnManager.CurrentPlayer;
        if (playerId != null && context.GamePlayers.TryGetValue(playerId, out var pState))
        {
            int cardsNeeded = Math.Min(3, 5 - pState.Hand.Count);

            for (int i = 0; i < cardsNeeded; i++)
            {
                if (context.State.Deck.Count == 0) break;
                var card = context.State.Deck[0];
                context.State.Deck.RemoveAt(0);
                pState.Hand.Add(card);
            }
        }

        // Check game-over condition: deck empty and no player has playable cards
        if (context.State.Deck.Count == 0)
        {
            bool hasMoves = false;
            foreach (var p in context.GamePlayers.Values)
            {
                if (p.Hand.Any(c => c.IsPlayable(context, p)))
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

        // Advance to next player's turn
        context.State.TurnManager.NextTurn();
        context.State.TurnCount++;

        var newPlayerId = context.State.TurnManager.CurrentPlayer;
        if (newPlayerId != null && context.GamePlayers.TryGetValue(newPlayerId, out var newPState))
        {
            newPState.HasPlayedCardThisTurn = false;
        }

        context.State.Phase = OperatorGamePhase.Play;
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
    }

    public Result OnExit(OperatorGameContext context)
    {
        return Result.Success;
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> HandleCommand(OperatorGameContext context, OperatorCommand command)
    {
        // Draw is now automatic — no commands accepted in this transient state
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Draw phase is automatic.");
    }
}
