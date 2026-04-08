using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class TargetedActionCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards,
    ActionCard actionCard)
    : BaseActionCommand(context, playCommand, playedCards)
{
    private readonly ActionCard _actionCard = actionCard;

    public override bool RequiresReaction => !string.IsNullOrEmpty(PlayCommand.TargetPlayerId);

    public override IEnumerable<string> GetReactionTargetIds() =>
        !string.IsNullOrEmpty(PlayCommand.TargetPlayerId) ? [PlayCommand.TargetPlayerId] : [];

    public override void Execute()
    {
        var pState = Context.GamePlayers[PlayCommand.PlayerId];
        var actionBlocked = Context.State.PlayerReactions.Any(r => r.ReactionCard != null);

        var val = CalculateNumberValue();
        var numbers = PlayedCards.OfType<NumberCard>().ToList();

        var playContext = new CardPlayContext(
            GameContext: Context,
            ThisPlayer: pState,
            TargetPlayerId: PlayCommand.TargetPlayerId,
            CombinedNumberValue: val,
            PairedNumbers: numbers,
            ActionBlocked: actionBlocked
        );

        _actionCard.Play(playContext);
    }

    private decimal CalculateNumberValue()
    {
        decimal val = 0;
        var numbers = PlayedCards.OfType<NumberCard>().ToList();
        foreach (var num in numbers)
        {
            val = val * 10 + num.NumberValue;
        }
        return val;
    }
}
