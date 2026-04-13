using System;
using System.Linq;
using System.Collections.Generic;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.States;

public class PlayPhaseState : IOperatorGameState, ITimedGameState<OperatorGameContext, OperatorCommand>
{
    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> OnEnter(OperatorGameContext context)
    {
        context.State.StateStartTime = DateTimeOffset.UtcNow;

        // Snapshot hand at turn start so the Draw phase can diff against it for new-card animation
        var playerId = context.State.TurnManager.CurrentPlayer;
        if (playerId != null && context.GamePlayers.TryGetValue(playerId, out var currentPlayer))
        {
            currentPlayer.PreDrawCardIds = new System.Collections.Generic.HashSet<Guid>(currentPlayer.Hand.Select(c => c.Id));
        }

        // Clear transient UI states
        foreach (var player in context.GamePlayers.Values)
        {
            player.IsDivideBroken = false;
            player.IsBeingStolenFrom = false;
        }

        // Clear expired audits
        foreach (var player in context.GamePlayers.Values)
        {
            if (player.IsAudited && context.State.TurnCount >= player.AuditExpiresTurnCount)
            {
                player.IsAudited = false;
            }
        }

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
            var pState = GetPlayerState(context, skip.PlayerId);
            if (pState == null) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
            
            if (!pState.Hand.Any(c => c.IsPlayable(context, pState)))
            {
                return TransitionToDrawPhase(context);
            }
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Cannot skip if you have playable cards.");
        }

        if (command is EndTurnCommand end)
        {
            var pState = GetPlayerState(context, end.PlayerId);
            if (pState == null) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");

            if (!pState.HasPlayedCardThisTurn)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Cannot end turn before playing a card.");
            if (pState.Hand.Count > context.State.Config.MaxHandSize)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError($"Cannot end turn with more than {context.State.Config.MaxHandSize} cards.");

            return TransitionToDrawPhase(context);
        }

        if (command is PlayCardsCommand play)
        {
            if (play.CardIds.Count == 0)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Must play at least 1 card.");

            context.State.LastBlockedActionMessage = null;
            context.State.BlockedAttackerId = null;

            var pState = GetPlayerState(context, play.PlayerId);
            if (pState == null) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");

            var playedCards = new List<Card>();
            foreach (var id in play.CardIds)
            {
                var cardIdx = pState.Hand.FindIndex(c => c.Id == id);
                if (cardIdx == -1) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Card not found in hand.");
                playedCards.Add(pState.Hand[cardIdx]);
            }

            if (playedCards.Count == 1)
            {
                if (!playedCards[0].IsPlayable(context, pState))
                    return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Card is not playable.");
            }
            else if (playedCards.Count > 1)
            {
                bool allNumbers = playedCards.All(c => c is NumberCard);
                if (!allNumbers)
                {
                    var pairableCards = playedCards.OfType<IPairableCard>().ToList();
                    if (pairableCards.Count != 1)
                        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Multi-card plays with actions require exactly one pairable card.");
                    
                    var pairableCard = pairableCards[0];
                    var pairings = pairableCard.GetPotentialPairingCards(context, pState).ToList();
                    
                    var otherCards = playedCards.Where(c => c != (Card)pairableCard).ToList();
                    if (!otherCards.All(c => pairings.Contains(c)))
                        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid card pairing.");
                }
            }

            foreach (var c in playedCards)
            {
                pState.Hand.Remove(c);
                context.State.DiscardPile.Add(c);
            }

            pState.HasPlayedCardThisTurn = true;
            context.State.StateStartTime = DateTimeOffset.UtcNow;

            IGameActionCommand actionCommand;
            var actionCard = playedCards.OfType<ActionCard>().FirstOrDefault();
            if (actionCard != null)
            {
                actionCommand = actionCard.CreateCommand(context, play, playedCards);
            }
            else
            {
                var opCard = playedCards.OfType<OperatorCard>().FirstOrDefault();
                if (opCard != null && !string.IsNullOrEmpty(play.TargetPlayerId)
                    && play.TargetPlayerId != play.PlayerId)
                {
                    actionCommand = new OperatorCardCommand(context, play, playedCards, opCard);
                }
                else
                {
                    actionCommand = new StandardPlayCommand(context, play, playedCards);
                }
            }

            if (actionCommand.RequiresReaction)
            {
                actionCommand.SetupPendingState();
                context.State.PendingGameActionCommand = actionCommand;
                context.State.ReactionTargetPlayerIds = new HashSet<string>(actionCommand.GetReactionTargetIds());
                context.State.PlayerReactions.Clear();
                context.State.Phase = OperatorGamePhase.Reaction;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
            }
            else
            {
                actionCommand.Execute();
            }

            if (pState.Hand.Count == 0)
            {
                return TransitionToDrawPhase(context);
            }

            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
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
            AutoPlayOnTimeout(context);
            return TransitionToDrawPhase(context);
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }

    private static ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> TransitionToDrawPhase(OperatorGameContext context)
    {
        context.State.Phase = OperatorGamePhase.Draw;
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
    }

    private static OperatorPlayerState? GetPlayerState(OperatorGameContext context, string playerId)
    {
        return context.GamePlayers.TryGetValue(playerId, out var pState) ? pState : null;
    }

    private static void AutoPlayOnTimeout(OperatorGameContext context)
    {
        var playerId = context.State.TurnManager.CurrentPlayer;
        if (playerId == null || !context.GamePlayers.TryGetValue(playerId, out var pState))
            return;

        if (pState.Hand.Count == 0)
            return;

        // Skip if hand is all Shields (valid skip per spec)
        if (pState.Hand.All(c => c is ShieldCard))
            return;

        // Try to auto-play one card: first number, then operator, then discard random
        var numberCard = pState.Hand.OfType<NumberCard>().FirstOrDefault();
        var operatorCard = pState.Hand.OfType<OperatorCard>().FirstOrDefault();

        if (numberCard != null)
        {
            pState.Hand.Remove(numberCard);
            context.State.DiscardPile.Add(numberCard);

            var playCommand = new PlayCardsCommand(playerId, new List<Guid> { numberCard.Id }, null);
            new StandardPlayCommand(context, playCommand, new List<Card> { numberCard }).Execute();
        }
        else if (operatorCard != null)
        {
            pState.Hand.Remove(operatorCard);
            context.State.DiscardPile.Add(operatorCard);

            var playCommand = new PlayCardsCommand(playerId, new List<Guid> { operatorCard.Id }, null);
            new StandardPlayCommand(context, playCommand, new List<Card> { operatorCard }).Execute();
        }
        else
        {
            // Discard a random card
            var idx = context.Rng.GetRandomInt(pState.Hand.Count);
            var discarded = pState.Hand[idx];
            pState.Hand.RemoveAt(idx);
            context.State.DiscardPile.Add(discarded);
        }

        // Discard extra cards to bring hand size down to max
        while (pState.Hand.Count > context.State.Config.MaxHandSize)
        {
            // Prefer discarding non-Shield cards
            var nonShieldIdx = pState.Hand.FindIndex(c => c is not ShieldCard);
            if (nonShieldIdx != -1)
            {
                context.State.DiscardPile.Add(pState.Hand[nonShieldIdx]);
                pState.Hand.RemoveAt(nonShieldIdx);
            }
            else
            {
                // All remaining are Shields — discard one
                context.State.DiscardPile.Add(pState.Hand[0]);
                pState.Hand.RemoveAt(0);
            }
        }
    }
}