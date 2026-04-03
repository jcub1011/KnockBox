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
                 c.ActionValue == CardAction.HostileTakeover || c.ActionValue == CardAction.Audit ||
                 c.ActionValue == CardAction.HotPotato || c.ActionValue == CardAction.FlashFlood));

            bool hasTargetedOperator = playedCards.Any(c => c.Type == CardType.Operator)
                && !string.IsNullOrEmpty(play.TargetPlayerId)
                && play.TargetPlayerId != play.PlayerId;

            if ((hasTargetedAction || hasTargetedOperator) && !string.IsNullOrEmpty(play.TargetPlayerId))
            {
                context.State.PendingActionCommand = play;
                context.State.ReactionTargetPlayerId = play.TargetPlayerId;

                // If Hot Potato is in play, extract the number card for redirect tracking
                bool hasHotPotato = playedCards.Any(c => c.Type == CardType.Action && c.ActionValue == CardAction.HotPotato);
                if (hasHotPotato)
                {
                    var numbers = playedCards.Where(c => c.Type == CardType.Number).ToList();
                    if (numbers.Count > 0)
                    {
                        var hotPotatoNum = numbers.Last();
                        context.State.PendingHotPotatoCard = hotPotatoNum;
                        // Remove from discard — it will be resolved by ReactionState
                        var inDiscard = context.State.DiscardPile.FindLastIndex(c => c.Id == hotPotatoNum.Id);
                        if (inDiscard != -1) context.State.DiscardPile.RemoveAt(inDiscard);
                    }
                }

                context.State.Phase = OperatorGamePhase.Reaction;
                return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new ReactionState());
            }

            // Resolve immediate logic if no reaction needed
            ResolvePlayedCards(context, play, playedCards, false);

            context.State.Phase = OperatorGamePhase.Draw;
            return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromValue(new DrawPhaseState());
        }

        return ValueResult<IGameState<OperatorGameContext, OperatorCommand>?>.FromError("Invalid command for PlayPhase.");
    }

    public static void ResolvePlayedCards(OperatorGameContext context, PlayCardsCommand play, List<Card> playedCards, bool actionBlocked)
    {
        var pState = context.GamePlayers[play.PlayerId];
        var actionCards = playedCards.Where(c => c.Type == CardType.Action).ToList();
        var numbers = playedCards.Where(c => c.Type == CardType.Number).ToList();

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
                    effectiveScoreTarget = play.TargetPlayerId;
                }
                else if (action.ActionValue == CardAction.Steal && play.TargetPlayerId != null)
                {
                    context.ResolveSteal(play.PlayerId, play.TargetPlayerId);
                }
                else if (action.ActionValue == CardAction.HotPotato && play.TargetPlayerId != null && numbers.Any())
                {
                    // For Hot Potato, give the last number card to target
                    var hotPotatoNum = numbers.Last();
                    context.ResolveHotPotato(play.TargetPlayerId, hotPotatoNum);
                    numbers.Remove(hotPotatoNum);
                    
                    // The given card should be removed from discard pile because it's now in the target's hand
                    var inDiscard = context.State.DiscardPile.FindLastIndex(c => c.Id == hotPotatoNum.Id);
                    if (inDiscard != -1) context.State.DiscardPile.RemoveAt(inDiscard);
                }
                else if (action.ActionValue == CardAction.FlashFlood && play.TargetPlayerId != null)
                {
                    context.ResolveFlashFlood(play.TargetPlayerId);
                }
                else if (action.ActionValue == CardAction.HostileTakeover && play.TargetPlayerId != null)
                {
                    context.ResolveHostileTakeover(play.PlayerId, play.TargetPlayerId);
                }
                else if (action.ActionValue == CardAction.Audit && play.TargetPlayerId != null)
                {
                    context.ResolveAudit(play.TargetPlayerId);
                }
            }
        }

        // Apply standard number score logic if not consumed by CookTheBooks
        if (numbers.Any())
        {
            if (context.GamePlayers.TryGetValue(effectiveScoreTarget, out var targetPlayerState))
            {
                var (newScore, newOp) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, targetPlayerState.ActiveOperator, val);
                targetPlayerState.CurrentPoints = newScore;
                targetPlayerState.ActiveOperator = newOp;
                targetPlayerState.ScoreTimestamp = DateTimeOffset.UtcNow;
            }
        }

        // Apply operator changes — target another player if specified, otherwise self
        var opCard = playedCards.LastOrDefault(c => c.Type == CardType.Operator);
        if (opCard.Type == CardType.Operator)
        {
            bool operatorTargetsOther = !string.IsNullOrEmpty(play.TargetPlayerId) && play.TargetPlayerId != play.PlayerId;
            string opTargetId = operatorTargetsOther ? play.TargetPlayerId! : play.PlayerId;

            if (!actionBlocked || !operatorTargetsOther)
            {
                if (context.GamePlayers.TryGetValue(opTargetId, out var opTarget) && !opTarget.IsAudited)
                {
                    opTarget.ActiveOperator = opCard.OperatorValue;
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

        // Skip if hand is all Shields (valid skip per spec)
        if (pState.Hand.All(c => c.Type == CardType.Action && c.ActionValue == CardAction.Shield))
            return;

        // Try to auto-play one card: first number, then operator, then discard random
        var numberCard = pState.Hand.FirstOrDefault(c => c.Type == CardType.Number);
        var operatorCard = pState.Hand.FirstOrDefault(c => c.Type == CardType.Operator);

        if (numberCard.Id != Guid.Empty)
        {
            pState.Hand.Remove(numberCard);
            context.State.DiscardPile.Add(numberCard);

            var playCommand = new PlayCardsCommand(playerId, new List<Guid> { numberCard.Id }, null);
            ResolvePlayedCards(context, playCommand, new List<Card> { numberCard }, false);
        }
        else if (operatorCard.Id != Guid.Empty)
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

        // Discard extra cards to bring hand size down to 5
        while (pState.Hand.Count > 5)
        {
            // Prefer discarding non-Shield cards
            var nonShieldIdx = pState.Hand.FindIndex(c => c.Type != CardType.Action || c.ActionValue != CardAction.Shield);
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