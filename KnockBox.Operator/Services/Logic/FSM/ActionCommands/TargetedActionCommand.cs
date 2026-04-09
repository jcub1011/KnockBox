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

    public override bool RequiresReaction => GetReactionTargetIds().Any();

    public override IEnumerable<string> GetReactionTargetIds()
    {
        if (string.IsNullOrEmpty(PlayCommand.TargetPlayerId) || PlayCommand.TargetPlayerId == PlayCommand.PlayerId)
            return [];

        if (_actionCard.IsOperatorOnlyAction)
        {
            if (Context.GamePlayers.TryGetValue(PlayCommand.TargetPlayerId, out var target) && target.IsAudited)
                return [];
        }

        return [PlayCommand.TargetPlayerId];
    }

    public override void Execute()
    {
        if (!Context.GamePlayers.TryGetValue(PlayCommand.PlayerId, out var pState))
            return;

        var actionBlocked = Context.State.PlayerReactions.Any(r => r.ReactionCard != null);
        LogPlay(actionBlocked);

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
}
