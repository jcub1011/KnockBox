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

public abstract class Card
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The icon for this card.
    /// </summary>
    /// <returns></returns>
    public abstract string CardIcon();

    /// <summary>
    /// The tooltip name of this card.
    /// </summary>
    /// <returns></returns>
    public abstract string TooltipName();

    /// <summary>
    /// The tooltip description of this card.
    /// </summary>
    /// <returns></returns>
    public abstract string TooltipDescription();
}

public class NumberCard(decimal numberValue = 0m) : Card
{
    public decimal NumberValue { get; init; } = numberValue;

    public override string CardIcon()
        => $"{NumberValue:N0}";

    public override string TooltipName()
        => $"Number {NumberValue:N0}";

    public override string TooltipDescription()
        => "Play number cards to modify scores. Stack multiple numbers to form larger values (e.g. 3 + 7 = 37).";
}

public class OperatorCard(CardOperator operatorValue = CardOperator.None) 
    : Card, ITargetableCard, IBlockableCard
{
    public CardOperator OperatorValue { get; init; } = operatorValue;

    public override string CardIcon()
    {
        return OperatorValue switch
        {
            CardOperator.Add => "+",
            CardOperator.Subtract => "-",
            CardOperator.Multiply => "\u00d7",
            CardOperator.Divide => "\u00f7",
            _ => "?"
        };
    }

    public override string TooltipName()
    {
        return OperatorValue switch
        {
            CardOperator.Add => "Add (+)",
            CardOperator.Subtract => "Subtract (-)",
            CardOperator.Multiply => "Multiply (\u00d7)",
            CardOperator.Divide => "Divide (\u00f7)",
            _ => "Unknown Operator"
        };
    }

    public override string TooltipDescription()
    {
        return OperatorValue switch
        {
            CardOperator.Add => "Sets a player's active operator to Add. Future number cards will be added to their score.",
            CardOperator.Subtract => "Sets a player's active operator to Subtract. Future number cards will be subtracted from their score.",
            CardOperator.Multiply => "Sets a player's active operator to Multiply. Future number cards will multiply their score.",
            CardOperator.Divide => "Sets a player's active operator to Divide. Future number cards will divide their score.",
            _ => "Changes a player's active operator."
        };
    }

    public IEnumerable<Card> GetPotentialReactionCards(OperatorPlayerState playerState)
    {
        // Only shield cards can block operators
        return playerState.Hand.Where((card) 
            => card is ActionCard action && action.ActionValue == CardAction.Shield);
    }

    public IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context)
    {
        // Can't replace active operator if operator are the same type
        return context.GamePlayers.Values.Where((player)
            => player.ActiveOperator != OperatorValue);
    }
}

public abstract class ActionCard(CardAction actionValue = CardAction.None) 
    : Card, ITargetableCard, IBlockableCard
{
    public CardAction ActionValue { get; init; } = actionValue;

    public abstract IEnumerable<Card> GetPotentialReactionCards(OperatorPlayerState playerState);
    public abstract IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context);
}