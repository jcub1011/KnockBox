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
            if (!context.GamePlayers.TryGetValue(skip.PlayerId, out var pState))
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
            if (!pState.Hand.Any(c => c.IsPlayable(context, pState)))
            {
                context.State.Phase = OperatorGamePhase.Draw;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
            }
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Cannot skip if you have playable cards.");
        }

        if (command is EndTurnCommand end)
        {
            if (!context.GamePlayers.TryGetValue(end.PlayerId, out var pState))
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
            if (!pState.HasPlayedCardThisTurn)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Cannot end turn before playing a card.");
            if (pState.Hand.Count > context.State.Config.MaxHandSize)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError($"Cannot end turn with more than {context.State.Config.MaxHandSize} cards.");

            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        if (command is PlayCardsCommand play)
        {
            if (play.CardIds.Count == 0)
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Must play at least 1 card.");

            context.State.LastBlockedActionMessage = null;
            context.State.BlockedAttackerId = null;

            if (!context.GamePlayers.TryGetValue(play.PlayerId, out var pState))
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Player not found.");
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

            bool hasTargetedAction = playedCards.Any(c => c is ActionCard && c is ITargetableCard);

            bool hasTargetedOperator = playedCards.Any(c => c.Type == CardType.Operator)
                && !string.IsNullOrEmpty(play.TargetPlayerId)
                && play.TargetPlayerId != play.PlayerId;

            bool isBlueShell = playedCards.Any(c => c is BlueShellCard);

            if (isBlueShell)
            {
                var zeroPlayers = context.GamePlayers.Values.Where(p => p.CurrentPoints == 0m).Select(p => p.UserId).ToList();
                if (zeroPlayers.Any())
                {
                    context.State.PendingActionCommand = play;
                    context.State.ReactionTargetPlayerIds = new HashSet<string>(zeroPlayers);
                    context.State.PlayerReactions.Clear();
                    context.State.Phase = OperatorGamePhase.Reaction;
                    return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
                }
            }

            if ((hasTargetedAction || hasTargetedOperator) && !string.IsNullOrEmpty(play.TargetPlayerId) && play.TargetPlayerId != play.PlayerId)
            {
                if (!context.GamePlayers.ContainsKey(play.TargetPlayerId))
                    return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Target player not found.");

                context.State.PendingActionCommand = play;
                context.State.ReactionTargetPlayerIds = new HashSet<string> { play.TargetPlayerId };

                // If Hot Potato is in play, extract the number cards for redirect tracking
                bool hasHotPotato = playedCards.Any(c => c is HotPotatoCard);
                if (hasHotPotato)
                {
                    var numbers = playedCards.Where(c => c.Type == CardType.Number).ToList();
                    if (numbers.Count > 0)
                    {
                        context.State.PendingHotPotatoCards = new List<Card>(numbers);
                        // Remove from discard — it will be resolved by ReactionState
                        foreach (var num in numbers)
                        {
                            var inDiscard = context.State.DiscardPile.FindLastIndex(c => c.Id == num.Id);
                            if (inDiscard != -1) context.State.DiscardPile.RemoveAt(inDiscard);
                        }
                    }
                }

                context.State.Phase = OperatorGamePhase.Reaction;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
            }

            // Resolve immediate logic if no reaction needed
            ResolvePlayedCards(context, play, playedCards, false);

            if (pState.Hand.Count == 0)
            {
                context.State.Phase = OperatorGamePhase.Draw;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
            }

            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for PlayPhase.");
    }

    public static void ResolvePlayedCards(OperatorGameContext context, PlayCardsCommand play, List<Card> playedCards, bool actionBlocked)
    {
        if (!context.GamePlayers.TryGetValue(play.PlayerId, out var pState))
            return;
        
        var actionCards = playedCards.OfType<ActionCard>().ToList();
        var numbers = playedCards.OfType<NumberCard>().ToList();
        var opCard = playedCards.OfType<OperatorCard>().LastOrDefault();

        // Log the base play
        string sourceName = context.State.Players.FirstOrDefault(p => p.Id == play.PlayerId)?.Name ?? "Unknown";
        string targetName = play.TargetPlayerId != null ? context.State.Players.FirstOrDefault(p => p.Id == play.TargetPlayerId)?.Name ?? "Unknown" : "themselves";
        string cardNames = string.Join(", ", playedCards.Select(c => c.TooltipName()));
        
        context.State.ActionLog.Add(new ActionLogEntry(
            $"{sourceName} played {cardNames} targeting {targetName}",
            DateTimeOffset.UtcNow,
            play.PlayerId,
            play.TargetPlayerId));

        if (actionBlocked)
        {
            context.State.ActionLog.Add(new ActionLogEntry(
                $"{targetName} blocked the action!",
                DateTimeOffset.UtcNow,
                play.TargetPlayerId,
                play.PlayerId));
            // Note: We don't return here because some effects (like self-operators or non-targeted parts) might still resolve
        }

        // Calculate combined number value
        decimal val = 0;
        if (numbers.Any())
        {
            foreach (var num in numbers)
            {
                val = val * 10 + num.NumberValue;
            }
        }

        string effectiveScoreTarget = play.PlayerId;

        // Non-targeted or resolved targeted actions
        foreach (var action in actionCards)
        {
            if (action.ActionValue == CardAction.Comp)
            {
                context.ResolveComp(play.PlayerId);
            }
            else if (action.ActionValue == CardAction.MarketCrash)
            {
                context.ResolveMarketCrash();
            }
            else if (!actionBlocked)
            {
                if (action.ActionValue == CardAction.CookTheBooks && numbers.Any())
                {
                    context.ResolveCookTheBooks(play.PlayerId, val);
                    numbers.Clear(); // prevent standard score calculation below
                }
                else if (action.ActionValue == CardAction.LiabilityTransfer && play.TargetPlayerId != null)
                {
                    context.ResolveLiabilityTransfer(play.TargetPlayerId, numbers.Cast<Card>().ToList());
                    numbers.Clear(); // prevent standard score calculation below
                }
                else if (action.ActionValue == CardAction.Steal && play.TargetPlayerId != null)
                {
                    if (context.GamePlayers.TryGetValue(play.TargetPlayerId, out var target))
                    {
                        target.IsBeingStolenFrom = true;
                    }
                    context.ResolveSteal(play.PlayerId, play.TargetPlayerId);
                }
                else if (action.ActionValue == CardAction.HotPotato && play.TargetPlayerId != null && numbers.Any())
                {
                    context.ResolveHotPotato(play.TargetPlayerId, val);
                    numbers.Clear();
                }
                else if (action.ActionValue == CardAction.FlashFlood)
                {
                    context.ResolveFlashFlood();
                }
                else if (action.ActionValue == CardAction.HostileTakeover && play.TargetPlayerId != null)
                {
                    context.ResolveHostileTakeover(play.PlayerId, play.TargetPlayerId);
                }
                else if (action.ActionValue == CardAction.Audit && play.TargetPlayerId != null)
                {
                    context.ResolveAudit(play.TargetPlayerId);
                }
                else if (action.ActionValue == CardAction.Surcharge && play.TargetPlayerId != null && numbers.Any())
                {
                    context.ResolveSurcharge(play.TargetPlayerId, val);
                    numbers.Clear();
                }
                else if (action.ActionValue == CardAction.BlueShell)
                {
                    context.ResolveBlueShell(context.State.PlayerReactions
                        .Where(r => r.ReactionCard != null)
                        .Select(r => r.PlayerId)
                        .ToHashSet());
                }
            }
        }

        // Apply standard number score logic if not consumed by CookTheBooks
        if (numbers.Any())
        {
            if (context.GamePlayers.TryGetValue(effectiveScoreTarget, out var targetPlayerState))
            {
                // Divide breaking mechanic
                if (targetPlayerState.ActiveOperator == CardOperator.Divide)
                {
                    targetPlayerState.DivideUses++;
                    if (targetPlayerState.DivideUses >= 4 || val == 0m)
                    {
                        targetPlayerState.IsDivideBroken = true;
                        targetPlayerState.ActiveOperator = CardOperator.Add;
                        targetPlayerState.DivideUses = 0;
                        
                        string targetPlayerName = context.State.Players.FirstOrDefault(p => p.Id == targetPlayerState.UserId)?.Name ?? "Unknown";
                        context.State.ActionLog.Add(new ActionLogEntry(
                            $"The Divide operator shattered! {targetPlayerName}'s operator reverted to Plus.",
                            DateTimeOffset.UtcNow,
                            null,
                            targetPlayerState.UserId));

                        if (val == 0m)
                        {
                            targetPlayerState.CurrentPoints = 0m;
                        }
                        else
                        {
                            // Resolve the last divide before it breaks? 
                            // Usually "breaking" implies it failed to work or worked one last time.
                            // The prompt says "animation showing divide operator breaking when dividing by zero or when it is used 4 times (broken divide reverts to a Plus)".
                            // I'll assume it resolves the current action THEN breaks, unless it's divide by zero.
                            var (newScore, newOp) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, CardOperator.Divide, val);
                            targetPlayerState.CurrentPoints = newScore;
                        }
                    }
                    else
                    {
                        var (newScore, newOp) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, targetPlayerState.ActiveOperator, val);
                        targetPlayerState.CurrentPoints = newScore;
                        targetPlayerState.ActiveOperator = newOp;
                    }
                }
                else
                {
                    var (newScore, newOp) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, targetPlayerState.ActiveOperator, val);
                    targetPlayerState.CurrentPoints = newScore;
                    targetPlayerState.ActiveOperator = newOp;
                }
                targetPlayerState.ScoreTimestamp = DateTimeOffset.UtcNow;
            }
        }

        // Apply operator changes — target another player if specified, otherwise self
        if (opCard != null)
        {
            bool operatorTargetsOther = !string.IsNullOrEmpty(play.TargetPlayerId) && play.TargetPlayerId != play.PlayerId;
            string opTargetId = operatorTargetsOther ? play.TargetPlayerId! : play.PlayerId;

            if (!actionBlocked || !operatorTargetsOther)
            {
                if (context.GamePlayers.TryGetValue(opTargetId, out var opTarget) && !opTarget.IsAudited)
                {
                    // Toggling logic
                    if (opTarget.ActiveOperator == opCard.OperatorValue)
                    {
                        opTarget.ActiveOperator = opCard.OperatorValue switch
                        {
                            CardOperator.Add => CardOperator.Subtract,
                            CardOperator.Subtract => CardOperator.Add,
                            CardOperator.Multiply => CardOperator.Divide,
                            CardOperator.Divide => CardOperator.Multiply,
                            _ => opCard.OperatorValue
                        };
                        
                        string targetNameLog = context.State.Players.FirstOrDefault(p => p.Id == opTarget.UserId)?.Name ?? "Unknown";
                        context.State.ActionLog.Add(new ActionLogEntry(
                            $"Operator toggled to opposite! {targetNameLog} is now {opTarget.ActiveOperator}.",
                            DateTimeOffset.UtcNow,
                            null,
                            opTarget.UserId));
                    }
                    else
                    {
                        opTarget.ActiveOperator = opCard.OperatorValue;
                    }
                    
                    // Reset divide uses if operator changed
                    if (opTarget.ActiveOperator != CardOperator.Divide)
                    {
                        opTarget.DivideUses = 0;
                    }
                }
            }
        }
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
            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(null);
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
            ResolvePlayedCards(context, playCommand, new List<Card> { numberCard }, false);
        }
        else if (operatorCard != null)
        {
            pState.Hand.Remove(operatorCard);
            context.State.DiscardPile.Add(operatorCard);

            var playCommand = new PlayCardsCommand(playerId, new List<Guid> { operatorCard.Id }, null);
            ResolvePlayedCards(context, playCommand, new List<Card> { operatorCard }, false);
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