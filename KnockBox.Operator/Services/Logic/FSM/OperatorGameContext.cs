using KnockBox.Operator.Models;
using KnockBox.Operator.Services.State;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace KnockBox.Operator.Services.Logic.FSM;

public class OperatorGameContext(OperatorGameState state)
{
    public OperatorGameState State { get; } = state;

    public ConcurrentDictionary<string, OperatorPlayerState> GamePlayers => State.GamePlayers;

    public static List<Card> GenerateDeck(int playerCount)
    {
        var deck = new List<Card>();
        int deckCount = (playerCount + 3) / 4;

        for (int i = 0; i < deckCount; i++)
        {
            AddBaseDeck(deck);
        }

        return deck.OrderBy(_ => Guid.NewGuid()).ToList();
    }

    private static void AddBaseDeck(List<Card> deck)
    {
        // Numbers (40)
        AddCards(deck, CardType.Number, 0m, 2);
        AddCards(deck, CardType.Number, 1m, 2);
        AddCards(deck, CardType.Number, 2m, 3);
        AddCards(deck, CardType.Number, 3m, 3);
        AddCards(deck, CardType.Number, 4m, 4);
        AddCards(deck, CardType.Number, 5m, 4);
        AddCards(deck, CardType.Number, 6m, 5);
        AddCards(deck, CardType.Number, 7m, 5);
        AddCards(deck, CardType.Number, 8m, 6);
        AddCards(deck, CardType.Number, 9m, 6);

        // Operators (20)
        AddCards(deck, CardType.Operator, CardOperator.Add, 8);
        AddCards(deck, CardType.Operator, CardOperator.Subtract, 8);
        AddCards(deck, CardType.Operator, CardOperator.Multiply, 2);
        AddCards(deck, CardType.Operator, CardOperator.Divide, 2);

        // Actions (20)
        AddCards(deck, CardType.Action, CardAction.Shield, 4);
        AddCards(deck, CardType.Action, CardAction.LiabilityTransfer, 3);
        AddCards(deck, CardType.Action, CardAction.CookTheBooks, 2);
        AddCards(deck, CardType.Action, CardAction.Comp, 2);
        AddCards(deck, CardType.Action, CardAction.Steal, 2);
        AddCards(deck, CardType.Action, CardAction.HotPotato, 2);
        AddCards(deck, CardType.Action, CardAction.FlashFlood, 2);
        AddCards(deck, CardType.Action, CardAction.HostileTakeover, 1);
        AddCards(deck, CardType.Action, CardAction.Audit, 1);
        AddCards(deck, CardType.Action, CardAction.MarketCrash, 1);
    }

    private static void AddCards(List<Card> deck, CardType type, decimal value, int count)
    {
        for (int i = 0; i < count; i++) deck.Add(new Card(type, NumberValue: value));
    }

    private static void AddCards(List<Card> deck, CardType type, CardOperator op, int count)
    {
        for (int i = 0; i < count; i++) deck.Add(new Card(type, OperatorValue: op));
    }

    private static void AddCards(List<Card> deck, CardType type, CardAction action, int count)
    {
        for (int i = 0; i < count; i++) deck.Add(new Card(type, ActionValue: action));
    }

    public static (decimal NewScore, CardOperator NewOperator) CalculateNewScore(decimal currentScore, CardOperator op, decimal value)
    {
        if (op == CardOperator.Divide && value == 0m)
        {
            return (0m, CardOperator.Add);
        }

        decimal newScore = op switch
        {
            CardOperator.Add => currentScore + value,
            CardOperator.Subtract => currentScore - value,
            CardOperator.Multiply => currentScore * value,
            CardOperator.Divide => currentScore / value,
            _ => currentScore
        };

        return (Math.Round(newScore, 1, MidpointRounding.AwayFromZero), op);
    }

    public void ResolveComp(string playerId)
    {
        if (GamePlayers.TryGetValue(playerId, out var player) && !player.IsAudited)
        {
            if (player.CurrentPoints < 0) player.ActiveOperator = CardOperator.Add;
            else if (player.CurrentPoints > 0) player.ActiveOperator = CardOperator.Subtract;
        }
    }

    public void ResolveMarketCrash()
    {
        foreach (var player in GamePlayers.Values)
        {
            if (!player.IsAudited) player.ActiveOperator = CardOperator.Divide;
        }
    }

    public void ResolveCookTheBooks(string playerId, decimal incomingValue)
    {
        if (GamePlayers.TryGetValue(playerId, out var player))
        {
            var (newScore, newOp) = CalculateNewScore(player.CurrentPoints, CardOperator.Divide, incomingValue);
            player.CurrentPoints = newScore;
            player.ScoreTimestamp = DateTimeOffset.UtcNow;
        }
    }

    public void ResolveSteal(string sourcePlayerId, string targetPlayerId)
    {
        if (GamePlayers.TryGetValue(sourcePlayerId, out var source) && GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            if (target.Hand.Count > 0)
            {
                var rand = new Random();
                var cardIdx = rand.Next(target.Hand.Count);
                var stolen = target.Hand[cardIdx];
                target.Hand.RemoveAt(cardIdx);
                source.Hand.Add(stolen);
            }
        }
    }

    public void ResolveHotPotato(string targetPlayerId, Card numberCard)
    {
        if (GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            target.Hand.Add(numberCard);
        }
    }

    public void ResolveFlashFlood(string targetPlayerId)
    {
        if (GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            for (int i = 0; i < 2; i++)
            {
                if (State.Deck.Count == 0 && State.DiscardPile.Count > 0)
                {
                    State.Deck.AddRange(State.DiscardPile.OrderBy(_ => Guid.NewGuid()));
                    State.DiscardPile.Clear();
                }
                if (State.Deck.Count > 0)
                {
                    target.Hand.Add(State.Deck[0]);
                    State.Deck.RemoveAt(0);
                }
            }
        }
    }

    public void ResolveHostileTakeover(string sourcePlayerId, string targetPlayerId)
    {
        if (GamePlayers.TryGetValue(sourcePlayerId, out var source) && GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            if (!source.IsAudited && !target.IsAudited)
            {
                (target.ActiveOperator, source.ActiveOperator) = (source.ActiveOperator, target.ActiveOperator);
            }
        }
    }

    public void ResolveAudit(string targetPlayerId)
    {
        if (GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            target.IsAudited = true;
        }
    }
}
