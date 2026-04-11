using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class OperatorCardCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards,
    OperatorCard operatorCard)
    : BaseActionCommand(context, playCommand, playedCards)
{
    private readonly OperatorCard _operatorCard = operatorCard;

    public override bool RequiresReaction => GetReactionTargetIds().Any();

    public override IEnumerable<string> GetReactionTargetIds()
    {
        if (string.IsNullOrEmpty(PlayCommand.TargetPlayerId)
            || PlayCommand.TargetPlayerId == PlayCommand.PlayerId)
            return [];

        if (Context.GamePlayers.TryGetValue(PlayCommand.TargetPlayerId, out var target)
            && target.IsAudited)
            return [];

        return [PlayCommand.TargetPlayerId];
    }

    public override void Execute()
    {
        if (!Context.GamePlayers.TryGetValue(PlayCommand.PlayerId, out var pState))
            return;

        var actionBlocked = Context.State.PlayerReactions.Any(r => r.ReactionCard != null);
        LogPlay(actionBlocked);

        var playContext = new CardPlayContext(
            GameContext: Context,
            ThisPlayer: pState,
            TargetPlayerId: PlayCommand.TargetPlayerId,
            CombinedNumberValue: 0,
            PairedNumbers: [],
            ActionBlocked: actionBlocked
        );

        var opResult = _operatorCard.Play(playContext);
        if (opResult.TryGetSuccess(out var opPlayResult)
            && opPlayResult.Toggled
            && opPlayResult.OperatorTargetId != null)
        {
            if (Context.GamePlayers.TryGetValue(opPlayResult.OperatorTargetId, out var opTarget))
            {
                string targetNameLog = GetPlayerName(opTarget.UserId);
                Context.State.ActionLog.Add(new ActionLogEntry(
                    $"Operator toggled to opposite! {targetNameLog} is now {opTarget.ActiveOperator}.",
                    DateTimeOffset.UtcNow,
                    null,
                    opTarget.UserId));
            }
        }
    }
}
