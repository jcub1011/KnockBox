using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class GlobalActionCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards,
    ActionCard actionCard)
    : BaseActionCommand(context, playCommand, playedCards)
{
    private readonly ActionCard _actionCard = actionCard;

    public override bool RequiresReaction => true;

    public override IEnumerable<string> GetReactionTargetIds() =>
        Context.GamePlayers.Keys.Where(id => id != PlayCommand.PlayerId);

    public override void Execute()
    {
        var pState = Context.GamePlayers[PlayCommand.PlayerId];

        var playContext = new CardPlayContext(
            GameContext: Context,
            ThisPlayer: pState,
            TargetPlayerId: null,
            CombinedNumberValue: 0,
            PairedNumbers: [],
            ActionBlocked: false
        );

        _actionCard.Play(playContext);
    }
}
