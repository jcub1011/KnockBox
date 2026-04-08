using System;
using System.Linq;
using System.Collections.Generic;
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

    private ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> TransitionAfterReaction(OperatorGameContext context)
    {
        var currentPlayerId = context.State.TurnManager.CurrentPlayer;
        if (currentPlayerId != null && context.GamePlayers.TryGetValue(currentPlayerId, out var pState) && pState.Hand.Count == 0)
        {
            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        context.State.Phase = OperatorGamePhase.Play;
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new PlayPhaseState());
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> HandleCommand(OperatorGameContext context, OperatorCommand command)
    {
        if (command.PlayerId != context.State.ReactionTargetPlayerId)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Not your reaction.");

        if (command is PlayReactionCommand react)
        {
            if (!context.GamePlayers.TryGetValue(react.PlayerId, out var pState))
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
            var shieldIdx = pState.Hand.FindIndex(c => c.Id == react.ShieldCardId && c is ShieldCard);

            if (shieldIdx == -1) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Shield not found.");

            var shield = pState.Hand[shieldIdx];
            pState.Hand.RemoveAt(shieldIdx);
            context.State.DiscardPile.Add(shield);

            string reactorName = context.State.Players.FirstOrDefault(p => p.Id == react.PlayerId)?.Name ?? "Unknown";
            string attackerName = context.State.Players.FirstOrDefault(p => p.Id == context.State.TurnManager.CurrentPlayer)?.Name ?? "Unknown";

            context.State.LastBlockedActionMessage = "Your action was blocked!";
            context.State.BlockedAttackerId = context.State.TurnManager.CurrentPlayer;
            context.State.ActionLog.Add(new ActionLogEntry(
                $"{reactorName} used a Shield to block {attackerName}'s action.",
                DateTimeOffset.UtcNow,
                react.PlayerId,
                context.State.TurnManager.CurrentPlayer));

            ResolvePendingAction(context, true);
            ClearPendingState(context);

            return TransitionAfterReaction(context);
        }
        else if (command is RedirectHotPotatoCommand redirect)
        {
            return HandleHotPotatoRedirect(context, redirect);
        }
        else if (command is PassReactionCommand)
        {
            ResolvePendingAction(context, false);
            ClearPendingState(context);

            return TransitionAfterReaction(context);
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for ReactionPhase.");
    }

    private ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> HandleHotPotatoRedirect(
        OperatorGameContext context, RedirectHotPotatoCommand redirect)
    {
        // Verify pending action is a Hot Potato
        if (context.State.PendingHotPotatoCards.Count == 0)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("No Hot Potato to redirect.");

        // Verify the reactor has a Hot Potato card
        if (!context.GamePlayers.TryGetValue(redirect.PlayerId, out var pState))
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
        var hpIdx = pState.Hand.FindIndex(c => c.Id == redirect.HotPotatoCardId && c is HotPotatoCard);

        if (hpIdx == -1)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Hot Potato card not found in hand.");

        // Verify new target exists and isn't the redirector
        if (redirect.NewTargetPlayerId == redirect.PlayerId)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Cannot redirect Hot Potato to yourself.");

        if (!context.GamePlayers.ContainsKey(redirect.NewTargetPlayerId))
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Target player not found.");

        // Consume the reactor's Hot Potato card
        var hpCard = pState.Hand[hpIdx];
        pState.Hand.RemoveAt(hpIdx);
        context.State.DiscardPile.Add(hpCard);

        // Redirect to new target — re-enter reaction state
        context.State.ReactionTargetPlayerId = redirect.NewTargetPlayerId;
        context.State.StateStartTime = DateTimeOffset.UtcNow;
        if (context.State.PendingActionCommand is PlayCardsCommand playCmd)
        {
            context.State.PendingActionCommand = playCmd with { TargetPlayerId = redirect.NewTargetPlayerId };
        }

        // Stay in ReactionState with new target (return null to stay, but we need a fresh state)
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
    }

    private void ResolvePendingAction(OperatorGameContext context, bool actionBlocked)
    {
        if (context.State.PendingActionCommand is PlayCardsCommand playCommand)
        {
            var playedCards = new List<Card>();
            foreach (var id in playCommand.CardIds)
            {
                var card = context.State.DiscardPile.FirstOrDefault(c => c.Id == id);
                if (card != null)
                {
                    playedCards.Add(card);
                }
            }

            bool isHotPotato = playedCards.Any(c => c is HotPotatoCard);
            bool isLiabilityTransfer = playedCards.Any(c => c is LiabilityTransferCard);

            if (actionBlocked && (context.State.PendingHotPotatoCards.Count > 0 || isLiabilityTransfer))
            {
                // Return cards to the sender's hand
                if (context.GamePlayers.TryGetValue(playCommand.PlayerId, out var sender))
                {
                    List<Card> cardsToReturn = new();
                    if (isLiabilityTransfer)
                    {
                        var numbers = playedCards.Where(c => c.Type == CardType.Number).ToList();
                        cardsToReturn.AddRange(numbers);
                        // Remove from discard and resolution list
                        foreach (var card in numbers)
                        {
                            context.State.DiscardPile.Remove(card);
                            playedCards.Remove(card);
                        }
                    }
                    else // Hot Potato
                    {
                        cardsToReturn.AddRange(context.State.PendingHotPotatoCards);
                        // Hot Potato numbers were already removed from discard in PlayPhaseState
                    }

                    if (cardsToReturn.Count > 0)
                    {
                        sender.Hand.AddRange(cardsToReturn);
                        
                        string senderName = context.State.Players.FirstOrDefault(p => p.Id == playCommand.PlayerId)?.Name ?? "Unknown";
                        string actionName = isLiabilityTransfer ? "Liability Transfer" : "Hot Potato";
                        context.State.ActionLog.Add(new ActionLogEntry(
                            $"{actionName} was blocked! The number cards were returned to {senderName}'s hand.",
                            DateTimeOffset.UtcNow,
                            null,
                            playCommand.PlayerId));
                    }
                }
            }
            else
            {
                // Re-add the Hot Potato number cards so ResolvePlayedCards can find them
                foreach (var hpCard in context.State.PendingHotPotatoCards)
                {
                    if (!playedCards.Any(c => c.Id == hpCard.Id))
                    {
                        playedCards.Add(hpCard);
                        context.State.DiscardPile.Add(hpCard);
                    }
                }
            }

            if (playedCards.Count > 0)
            {
                PlayPhaseState.ResolvePlayedCards(context, playCommand, playedCards, actionBlocked);
            }
        }
        context.State.PendingHotPotatoCards.Clear();
    }

    private static void ClearPendingState(OperatorGameContext context)
    {
        context.State.PendingActionCommand = null;
        context.State.ReactionTargetPlayerId = null;
        context.State.PendingHotPotatoCards.Clear();
    }

    private bool TargetCanReact(OperatorGameContext context)
    {
        if (context.State.ReactionTargetPlayerId != null && context.GamePlayers.TryGetValue(context.State.ReactionTargetPlayerId, out var targetPlayer))
        {
            Card? pendingAction = null;
            if (context.State.PendingActionCommand is PlayCardsCommand playCmd)
            {
                pendingAction = context.State.DiscardPile.FirstOrDefault(c => playCmd.CardIds.Contains(c.Id) && (c is ActionCard || c is OperatorCard));
            }
            if (pendingAction is IBlockableCard blockable)
            {
                return blockable.GetPotentialReactionCards(context, targetPlayer).Any();
            }
        }
        return false;
    }

    public ValueResult<TimeSpan> GetRemainingTime(OperatorGameContext context, DateTimeOffset now)
    {
        bool canReact = TargetCanReact(context);
        var elapsed = now - context.State.StateStartTime;

        if (!canReact)
        {
            var remaining = context.State.Config.NoReactionTimeout - elapsed;
            return ValueResult<TimeSpan>.FromValue(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }

        if (!context.State.Config.TimersEnabled) return ValueResult<TimeSpan>.FromValue(TimeSpan.MaxValue);

        var normalRemaining = context.State.Config.ReactionPhaseTimeout - elapsed;
        return ValueResult<TimeSpan>.FromValue(normalRemaining > TimeSpan.Zero ? normalRemaining : TimeSpan.Zero);
    }

    public ValueResult<IGameState<OperatorGameContext, OperatorCommand>?> Tick(OperatorGameContext context, DateTimeOffset now)
    {
        var elapsed = now - context.State.StateStartTime;
        bool canReact = TargetCanReact(context);

        bool isTimeout = false;
        if (!canReact && elapsed >= context.State.Config.NoReactionTimeout)
        {
            isTimeout = true;
        }
        else if (context.State.Config.TimersEnabled && elapsed >= context.State.Config.ReactionPhaseTimeout)
        {
            isTimeout = true;
        }

        if (isTimeout)
        {
            ResolvePendingAction(context, false);
            ClearPendingState(context);

            return TransitionAfterReaction(context);
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
    }
}
