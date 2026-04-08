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
    public override bool RequiresReaction => true;

    public override IEnumerable<string> GetReactionTargetIds() =>
        !string.IsNullOrEmpty(PlayCommand.TargetPlayerId) ? [PlayCommand.TargetPlayerId] : [];

    public override void Execute()
    {
        var pState = Context.GamePlayers[PlayCommand.PlayerId];
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

            string senderName = Context.State.Players.FirstOrDefault(p => p.Id == PlayCommand.PlayerId)?.Name ?? "Unknown";
            Context.State.ActionLog.Add(new ActionLogEntry(
                $"Liability Transfer was blocked! The number cards were returned to {senderName}'s hand.",
                DateTimeOffset.UtcNow,
                null,
                PlayCommand.PlayerId));
        }
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
