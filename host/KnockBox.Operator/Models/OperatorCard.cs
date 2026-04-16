using KnockBox.Core.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;

namespace KnockBox.Operator.Models;

public sealed class OperatorCard(CardOperator operatorValue = CardOperator.None)
    : Card, ITargetableCard, IBlockableCard
{
    public override CardType Type => CardType.Operator;

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
            CardOperator.Add => "Sets a player's active operator to Add. Future number cards will be added to their score. Playing this on a player with an Add active operator flips it to a Subtract.",
            CardOperator.Subtract => "Sets a player's active operator to Subtract. Future number cards will be subtracted from their score. Playing this on a player with a Subtract active operator flips it to a Add.",
            CardOperator.Multiply => "Sets a player's active operator to Multiply. Future number cards will multiply their score. Playing this on a player with a Multiply active operator flips it to a Divide.",
            CardOperator.Divide => "Sets a player's active operator to Divide. Future number cards will divide their score. Playing this on a player with a Divide active operator flips it to a Multiply.",
            _ => "Unknown operator."
        };
    }

    public IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        // Only shield cards can block operators
        return thisPlayer.Hand.Where((card)
            => card is ActionCard { ActionValue: CardAction.Shield });
    }

    public IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        // Can target any player as long as they are not audited
        return context.GamePlayers.Values.Where(player => !player.IsAudited);
    }

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => GetPotentialTargets(context, thisPlayer).Any();

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        bool targetsOther = !string.IsNullOrEmpty(ctx.TargetPlayerId) && ctx.TargetPlayerId != ctx.ThisPlayer.UserId;
        string opTargetId = targetsOther ? ctx.TargetPlayerId! : ctx.ThisPlayer.UserId;

        if (ctx.ActionBlocked && targetsOther)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());

        if (ctx.GameContext.GamePlayers.TryGetValue(opTargetId, out var opTarget) && !opTarget.IsAudited)
        {
            bool toggled = opTarget.ActiveOperator == OperatorValue;

            if (toggled)
            {
                opTarget.ActiveOperator = OperatorValue switch
                {
                    CardOperator.Add => CardOperator.Subtract,
                    CardOperator.Subtract => CardOperator.Add,
                    CardOperator.Multiply => CardOperator.Divide,
                    CardOperator.Divide => CardOperator.Multiply,
                    _ => OperatorValue
                };
            }
            else
            {
                opTarget.ActiveOperator = OperatorValue;
            }

            // Reset divide uses if operator changed
            if (opTarget.ActiveOperator != CardOperator.Divide)
            {
                opTarget.DivideUses = 0;
            }

            return ValueResult<CardPlayResult>.FromValue(new CardPlayResult(Toggled: toggled, OperatorTargetId: opTarget.UserId));
        }

        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }
}
