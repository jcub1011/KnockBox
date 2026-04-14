using KnockBox.Core.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class LiabilityTransferCard()
    : ActionCard(CardAction.LiabilityTransfer), ITargetableCard, IPairableCard
{
    public override string CardIcon() => "\ud83d\udce4";
    public override string TooltipName() => "Liability Transfer";
    public override string TooltipDescription() => "Play with number cards to pass them to the target player.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new LiabilityTransferCommand(context, playCommand, playedCards);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(player => player != thisPlayer);
    }

    public IEnumerable<Card> GetPotentialPairingCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is NumberCard);
    }

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => GetPotentialPairingCards(context, thisPlayer).Any();

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.PairedNumbers.Count == 0)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        if (ctx.ActionBlocked || ctx.TargetPlayerId == null)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
        Resolve(ctx.GameContext, ctx.TargetPlayerId, [.. ctx.PairedNumbers.Cast<Card>()]);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
    }

    public static void Resolve(OperatorGameContext context, string targetPlayerId, List<Card> numberCards)
    {
        if (context.GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            // Move number cards from discard pile to target's hand
            target.Hand.AddRange(numberCards);
            foreach (var card in numberCards)
            {
                context.State.DiscardPile.Remove(card);
            }
        }
    }
}
