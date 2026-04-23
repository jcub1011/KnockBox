using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class ShieldCard() : ActionCard(CardAction.Shield)
{
    public override string CardIcon() => "\ud83d\udee1\ufe0f";
    public override string TooltipName() => "Shield";
    public override string TooltipDescription() => "Hold in your hand to block an incoming targeted action. Used as a reaction to other action cards.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new TargetedActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];
    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer) => false; // Can only be used as a reaction
}
