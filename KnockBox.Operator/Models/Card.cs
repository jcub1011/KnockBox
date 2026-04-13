using KnockBox.Core.Extensions.Returns;
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
    MarketCrash,
    Surcharge,
    BlueShell
}

public static class CardOperatorExtensions
{
    public static string ToSymbol(this CardOperator op) => op switch
    {
        CardOperator.Add => "+",
        CardOperator.Subtract => "-",
        CardOperator.Multiply => "\u00d7",
        CardOperator.Divide => "\u00f7",
        _ => "?"
    };
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

public interface IPairableCard
{
    /// <summary>
    /// Gets the cards that can be played with this card.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="thisPlayer"></param>
    /// <returns></returns>
    IEnumerable<Card> GetPotentialPairingCards(OperatorGameContext context, OperatorPlayerState thisPlayer);
}

public interface IPlayableCard
{
    /// <summary>
    /// Checks if this card is able to be played.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="thisPlayer"></param>
    /// <returns></returns>
    bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer);

    /// <summary>
    /// Performs the action of this card.
    /// </summary>
    /// <param name="playContext"></param>
    ValueResult<CardPlayResult> Play(CardPlayContext playContext);
}

public abstract class Card : IPlayableCard
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

    /// <summary>
    /// Checks if this card can be played.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="thisPlayer"></param>
    /// <returns></returns>
    public abstract bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer);

    /// <summary>
    /// Performs the action of this card.
    /// </summary>
    /// <param name="playContext"></param>
    public virtual ValueResult<CardPlayResult> Play(CardPlayContext playContext) => ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
}
