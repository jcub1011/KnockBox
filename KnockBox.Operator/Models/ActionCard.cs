using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public abstract class ActionCard(CardAction actionValue = CardAction.None)
    : Card, IBlockableCard
{
    public override CardType Type => CardType.Action;

    public CardAction ActionValue { get; init; } = actionValue;

    public abstract override string CardIcon();
    public abstract override string TooltipName();
    public abstract override string TooltipDescription();

    public abstract IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer);

    public abstract IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards);
}
