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

    public override bool RequiresReaction => GetReactionTargetIds().Any();

    public override IEnumerable<string> GetReactionTargetIds()
    {
        var targets = Context.GamePlayers.Values.Where(p => p.UserId != PlayCommand.PlayerId);
        if (_actionCard.IsOperatorOnlyAction)
        {
            targets = targets.Where(p => !p.IsAudited);
        }
        return targets.Select(p => p.UserId);
    }

    public override void Execute()
    {
        if (!Context.GamePlayers.TryGetValue(PlayCommand.PlayerId, out var pState))
            return;

        LogPlay(false);

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
