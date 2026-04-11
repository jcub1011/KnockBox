using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class HotPotatoCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards)
    : BaseActionCommand(context, playCommand, playedCards)
{
    private List<Card> _pendingNumbers = [];

    public override bool RequiresReaction => GetReactionTargetIds().Any();

    public override IEnumerable<string> GetReactionTargetIds() =>
        (!string.IsNullOrEmpty(PlayCommand.TargetPlayerId) && PlayCommand.TargetPlayerId != PlayCommand.PlayerId) ? [PlayCommand.TargetPlayerId] : [];

    public override void SetupPendingState()
    {
        // Extract numbers from discard for redirect tracking
        _pendingNumbers = PlayedCards.Where(c => c.Type == CardType.Number).ToList();
        foreach (var num in _pendingNumbers)
        {
            var inDiscard = Context.State.DiscardPile.FindLastIndex(c => c.Id == num.Id);
            if (inDiscard != -1) Context.State.DiscardPile.RemoveAt(inDiscard);
        }
    }

    public override void Execute()
    {
        // Re-add numbers to discard for resolution
        foreach (var num in _pendingNumbers)
        {
            if (!Context.State.DiscardPile.Any(c => c.Id == num.Id))
                Context.State.DiscardPile.Add(num);
        }

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

        var hpCard = PlayedCards.OfType<HotPotatoCard>().FirstOrDefault();
        hpCard?.Play(playContext);
    }

    public override void OnBlocked()
    {
        // Return numbers to the sender's hand
        if (Context.GamePlayers.TryGetValue(PlayCommand.PlayerId, out var sender))
        {
            sender.Hand.AddRange(_pendingNumbers);

            string senderName = GetPlayerName(PlayCommand.PlayerId);
            Context.State.ActionLog.Add(new ActionLogEntry(
                $"Hot Potato was blocked! The number cards were returned to {senderName}'s hand.",
                DateTimeOffset.UtcNow,
                null,
                PlayCommand.PlayerId));
        }
    }
}
