using KnockBox.Core.Extensions.Collections;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace KnockBox.Operator.Services.Logic.FSM;

public class OperatorGameContext(OperatorGameState state, IRandomNumberService rng)
{
    public OperatorGameState State { get; } = state;
    public IRandomNumberService Rng { get; } = rng;

    public ConcurrentDictionary<string, OperatorPlayerState> GamePlayers => State.GamePlayers;

    public IFiniteStateMachine<OperatorGameContext, OperatorCommand> Fsm { get; set; } = null!;

    public static List<Card> GenerateDeck(int playerCount, IRandomNumberService rng)
    {
        var deck = new List<Card>();
        int deckCount = (playerCount + 3) / 4;

        for (int i = 0; i < deckCount; i++)
        {
            AddBaseDeck(deck);
        }

        deck.Shuffle(rng);
        return deck;
    }

    private static void AddBaseDeck(List<Card> deck)
    {
        // Numbers (48)
        AddCards(deck, CardType.Number, 0m, 2);
        AddCards(deck, CardType.Number, 1m, 2);
        AddCards(deck, CardType.Number, 2m, 4);
        AddCards(deck, CardType.Number, 3m, 4);
        AddCards(deck, CardType.Number, 4m, 5);
        AddCards(deck, CardType.Number, 5m, 5);
        AddCards(deck, CardType.Number, 6m, 6);
        AddCards(deck, CardType.Number, 7m, 6);
        AddCards(deck, CardType.Number, 8m, 7);
        AddCards(deck, CardType.Number, 9m, 7);

        // Operators (12)
        AddCards(deck, CardType.Operator, CardOperator.Add, 4);
        AddCards(deck, CardType.Operator, CardOperator.Subtract, 4);
        AddCards(deck, CardType.Operator, CardOperator.Multiply, 2);
        AddCards(deck, CardType.Operator, CardOperator.Divide, 0);

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
        AddCards(deck, CardType.Action, CardAction.Surcharge, 2);
        AddCards(deck, CardType.Action, CardAction.BlueShell, 2);
    }

    private static void AddCards(List<Card> deck, CardType type, decimal value, int count)
    {
        for (int i = 0; i < count; i++) deck.Add(new NumberCard(value));
    }

    private static void AddCards(List<Card> deck, CardType type, CardOperator op, int count)
    {
        for (int i = 0; i < count; i++) deck.Add(new OperatorCard(op));
    }

    private static void AddCards(List<Card> deck, CardType type, CardAction action, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Card card = action switch
            {
                CardAction.Shield => new ShieldCard(),
                CardAction.LiabilityTransfer => new LiabilityTransferCard(),
                CardAction.CookTheBooks => new CookTheBooksCard(),
                CardAction.Comp => new CompCard(),
                CardAction.Steal => new StealCard(),
                CardAction.HotPotato => new HotPotatoCard(),
                CardAction.FlashFlood => new FlashFloodCard(),
                CardAction.HostileTakeover => new HostileTakeoverCard(),
                CardAction.Audit => new AuditCard(),
                CardAction.MarketCrash => new MarketCrashCard(),
                CardAction.Surcharge => new SurchargeCard(),
                CardAction.BlueShell => new BlueShellCard(),
                _ => throw new NotImplementedException()
            };
            deck.Add(card);
        }
    }

    public void DealCards(OperatorPlayerState player, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (State.Deck.Count == 0) break;
            var card = State.Deck[0];
            State.Deck.RemoveAt(0);
            player.Hand.Add(card);
        }
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

}
