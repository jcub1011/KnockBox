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
            var pState = context.GamePlayers[react.PlayerId];
            var shieldIdx = pState.Hand.FindIndex(c => c.Id == react.ShieldCardId && c is ShieldCard);

            if (shieldIdx == -1) return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Shield not found.");

            var shield = pState.Hand[shieldIdx];
            pState.Hand.RemoveAt(shieldIdx);
            context.State.DiscardPile.Add(shield);

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
        if (context.State.PendingHotPotatoCard is null)
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("No Hot Potato to redirect.");

        // Verify the reactor has a Hot Potato card
        var pState = context.GamePlayers[redirect.PlayerId];
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

        // Stay in ReactionState with new target (return null to stay, but we need a fresh state)
        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
    }

    private void ResolvePendingAction(OperatorGameContext context, bool actionBlocked)
    {
        // Track Hot Potato number card ID so we can exclude it from ResolvePlayedCards
        Guid resolvedHotPotatoCardId = Guid.Empty;

        // If there's a pending Hot Potato, resolve it directly
        if (context.State.PendingHotPotatoCard is Card hotPotatoCard)
        {
            resolvedHotPotatoCardId = hotPotatoCard.Id;
            if (!actionBlocked && context.State.ReactionTargetPlayerId != null)
            {
                context.ResolveHotPotato(context.State.ReactionTargetPlayerId, hotPotatoCard);
            }
            context.State.PendingHotPotatoCard = null;
        }

        if (context.State.PendingActionCommand is PlayCardsCommand playCommand)
        {
            var playedCards = new List<Card>();
            foreach (var id in playCommand.CardIds)
            {
                // Skip the Hot Potato number card — it was already resolved above
                if (id == resolvedHotPotatoCardId) continue;

                var card = context.State.DiscardPile.FirstOrDefault(c => c.Id == id);
                if (card != null)
                {
                    playedCards.Add(card);
                }
            }

            if (playedCards.Count > 0)
            {
                PlayPhaseState.ResolvePlayedCards(context, playCommand, playedCards, actionBlocked);
            }
        }
    }

    private static void ClearPendingState(OperatorGameContext context)
    {
        context.State.PendingActionCommand = null;
        context.State.ReactionTargetPlayerId = null;
        context.State.PendingHotPotatoCard = null;
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
            var remaining = TimeSpan.FromSeconds(5) - elapsed;
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
        if (!canReact && elapsed >= TimeSpan.FromSeconds(5))
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
