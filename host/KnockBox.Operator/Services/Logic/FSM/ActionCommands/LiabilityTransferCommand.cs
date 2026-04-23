using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class LiabilityTransferCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards)
    : BaseActionCommand(context, playCommand, playedCards)
{
    public override bool RequiresReaction => GetReactionTargetIds().Any();

    public override IEnumerable<string> GetReactionTargetIds() =>
        (!string.IsNullOrEmpty(PlayCommand.TargetPlayerId) && PlayCommand.TargetPlayerId != PlayCommand.PlayerId) ? [PlayCommand.TargetPlayerId] : [];

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
            ActionBlocked: false
        );

        var ltCard = PlayedCards.OfType<LiabilityTransferCard>().FirstOrDefault();
        ltCard?.Play(playContext);
    }

    public override void OnBlocked()
    {
        // Return numbers to the sender's hand
        if (Context.GamePlayers.TryGetValue(PlayCommand.PlayerId, out var sender))
        {
            var numbers = PlayedCards.Where(c => c.Type == CardType.Number).ToList();
            sender.Hand.AddRange(numbers);
            foreach (var card in numbers)
            {
                Context.State.DiscardPile.Remove(card);
            }

            string senderName = GetPlayerName(PlayCommand.PlayerId);
            Context.State.ActionLog.Add(new ActionLogEntry(
                $"Liability Transfer was blocked! The number cards were returned to {senderName}'s hand.",
                DateTimeOffset.UtcNow,
                null,
                PlayCommand.PlayerId));
        }
    }
}
