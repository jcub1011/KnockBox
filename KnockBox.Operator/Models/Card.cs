using KnockBox.Operator.Services.Logic.FSM;

namespace KnockBox.Operator.Models;

public enum CardType
{
    Number,
    Operator,
    Action
}

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
    /// <param name="thisPlayer"></param>
    /// <returns></returns>
    IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer);
}

public interface IBlockableCard
{
    /// <summary>
    /// Gets the cards that can be used as a reaction to this card.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="thisPlayer"></param>
    /// <returns></returns>
    IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer);
}

public abstract class Card
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public abstract CardType Type { get; }

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
