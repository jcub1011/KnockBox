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
            CardOperator.Add => "Sets a player's active operator to Add. Future number cards will be added to their score.",
            CardOperator.Subtract => "Sets a player's active operator to Subtract. Future number cards will be subtracted from their score.",
            CardOperator.Multiply => "Sets a player's active operator to Multiply. Future number cards will multiply their score.",
            CardOperator.Divide => "Sets a player's active operator to Divide. Future number cards will divide their score.",
            _ => "Changes a player's active operator."
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
        // Can't replace active operator if operators are the same type
        return context.GamePlayers.Values.Where((player)
            => player.ActiveOperator != OperatorValue && !player.IsAudited);
    }
}
