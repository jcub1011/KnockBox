using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class BlueShellCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards)
    : BaseActionCommand(context, playCommand, playedCards)
{
    public override bool RequiresReaction => GetReactionTargetIds().Any();

    public override IEnumerable<string> GetReactionTargetIds() =>
        Context.GamePlayers.Values.Where(p => p.CurrentPoints == 0m).Select(p => p.UserId);

    public override void Execute()
    {
        var pState = Context.GamePlayers[PlayCommand.PlayerId];
        var blockedPlayerIds = Context.State.PlayerReactions
            .Where(r => r.ReactionCard != null)
            .Select(r => r.PlayerId)
            .ToHashSet();

        var playContext = new CardPlayContext(
            GameContext: Context,
            ThisPlayer: pState,
            TargetPlayerId: null,
            CombinedNumberValue: 0,
            PairedNumbers: [],
            ActionBlocked: false // Individual blocking handled inside BlueShellCard.Play via PlayerReactions
        );

        var bsCard = PlayedCards.OfType<BlueShellCard>().FirstOrDefault();
        bsCard?.Play(playContext);
    }
}
