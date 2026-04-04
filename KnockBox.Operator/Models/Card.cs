using KnockBox.Operator.Services.Logic.FSM;
using System;

namespace KnockBox.Operator.Models;

public enum CardOperator
{
    None,
    Add,
    Subtract,
    Multiply,
    Divide
}

public enum CardAction
{
    None,
    Shield,
    LiabilityTransfer,
    CookTheBooks,
    Comp,
    Steal,
    HotPotato,
    FlashFlood,
    HostileTakeover,
    Audit,
    MarketCrash
}

public interface ITargetableCard
{
    /// <summary>
    /// Gets the players that can be targeted by this card.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context);
}

public interface IBlockableCard
{
    /// <summary>
    /// Gets the cards that can be used as a reaction to this card.
    /// </summary>
    /// <param name="playerState"></param>
    /// <returns></returns>
    IEnumerable<Card> GetPotentialReactionCards(OperatorPlayerState playerState);
}

public class Card
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public class NumberCard(decimal numberValue = 0m)
{
    public decimal NumberValue { get; init; } = numberValue;
}

public abstract class OperatorCard(CardOperator operatorValue = CardOperator.None) 
    : Card, ITargetableCard, IBlockableCard
{
    public CardOperator OperatorValue { get; init; } = operatorValue;

    public abstract IEnumerable<Card> GetPotentialReactionCards(OperatorPlayerState playerState);
    public abstract IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context);
}

public abstract class ActionCard(CardAction actionValue = CardAction.None) 
    : Card, ITargetableCard, IBlockableCard
{
    public CardAction ActionValue { get; init; } = actionValue;

    public abstract IEnumerable<Card> GetPotentialReactionCards(OperatorPlayerState playerState);
    public abstract IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context);
}