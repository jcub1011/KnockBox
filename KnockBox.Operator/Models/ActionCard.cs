using KnockBox.Operator.Services.Logic.FSM;

namespace KnockBox.Operator.Models;

public abstract class ActionCard(CardAction actionValue = CardAction.None) 
    : Card, ITargetableCard, IBlockableCard
{
    public override CardType Type => CardType.Action;

    public CardAction ActionValue { get; init; } = actionValue;

    public abstract override string CardIcon();
    public abstract override string TooltipName();
    public abstract override string TooltipDescription();

    public abstract IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer);
    public abstract IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer);
}

public sealed class ShieldCard() : ActionCard(CardAction.Shield)
{
    public override string CardIcon() => "\ud83d\udee1\ufe0f";
    public override string TooltipName() => "Shield";
    public override string TooltipDescription() => "Hold in your hand to block an incoming targeted action. Used as a reaction to other action cards.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];
    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer) => [thisPlayer];
}

public sealed class LiabilityTransferCard() : ActionCard(CardAction.LiabilityTransfer)
{
    public override string CardIcon() => "\ud83d\udce4";
    public override string TooltipName() => "Liability Transfer";
    public override string TooltipDescription() => "Play with number cards to apply them to a target player's score instead of your own.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(player => player != thisPlayer);
    }
}

public sealed class CookTheBooksCard() : ActionCard(CardAction.CookTheBooks)
{
    public override string CardIcon() => "\ud83e\uddd1\u200d\ud83c\udf73";
    public override string TooltipName() => "Cook the Books";
    public override string TooltipDescription() => "Play with a number card to divide your score by that number.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => [thisPlayer];
}

public sealed class CompCard() : ActionCard(CardAction.Comp)
{
    public override string CardIcon() => "\u2696\ufe0f";
    public override string TooltipName() => "Comp";
    public override string TooltipDescription() => "Changes your operator. Positive scores get Subtract, negative scores get Add. Blocked by Audit.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];
    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        if (thisPlayer.IsAudited) return [];
        else return [thisPlayer];
    }
}

public sealed class StealCard() : ActionCard(CardAction.Steal)
{
    public override string CardIcon() => "\ud83d\udd75\ufe0f";
    public override string TooltipName() => "Steal";
    public override string TooltipDescription() => "Take a random card from a target player's hand and add it to yours.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => p.Hand.Count > 0 && p != thisPlayer);
    }
}

public sealed class HotPotatoCard() : ActionCard(CardAction.HotPotato)
{
    public override string CardIcon() => "\ud83e\udd54";
    public override string TooltipName() => "Hot Potato";
    public override string TooltipDescription() => "Play with a number card to force it into a target player's hand. They can redirect it with their own Hot Potato.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield or CardAction.HotPotato });
    }

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(player => player != thisPlayer);
    }
}

public sealed class FlashFloodCard() : ActionCard(CardAction.FlashFlood)
{
    public override string CardIcon() => "\ud83c\udf0a";
    public override string TooltipName() => "Flash Flood";
    public override string TooltipDescription() => "Forces a target player to draw 2 extra cards from the deck.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values;
    }
}

public sealed class HostileTakeoverCard() : ActionCard(CardAction.HostileTakeover)
{
    public override string CardIcon() => "\ud83e\udd1d";
    public override string TooltipName() => "Hostile Takeover";
    public override string TooltipDescription() => "Swap your active operator with a target player's. Blocked by Audit.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => !p.IsAudited && p != thisPlayer);
    }
}

public sealed class AuditCard() : ActionCard(CardAction.Audit)
{
    public override string CardIcon() => "\ud83d\udcdd";
    public override string TooltipName() => "Audit";
    public override string TooltipDescription() => "Locks a target player's operator for a full round. They cannot change it until the audit expires.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => !p.IsAudited);
    }
}

public sealed class MarketCrashCard() : ActionCard(CardAction.MarketCrash)
{
    public override string CardIcon() => "\ud83d\udcc9";
    public override string TooltipName() => "Market Crash";
    public override string TooltipDescription() => "Forces ALL players to switch to the Divide operator. Audited players are unaffected.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];

    public override IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => !p.IsAudited);
    }
}
