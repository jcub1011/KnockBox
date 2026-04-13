using KnockBox.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class SurchargeCard()
    : ActionCard(CardAction.Surcharge), ITargetableCard, IPairableCard
{
    public override string CardIcon() => "\ud83e\uddfe";
    public override string TooltipName() => "Surcharge";
    public override string TooltipDescription() => "Play with a number card to add that value directly to a target player's score, bypassing their operator.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new TargetedActionCommand(context, playCommand, playedCards, this);

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
        => GetPotentialPairingCards(context, thisPlayer).Any() && GetPotentialTargets(context, thisPlayer).Any();

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.PairedNumbers.Count == 0)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        if (ctx.ActionBlocked || ctx.TargetPlayerId == null)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
        Resolve(ctx.GameContext, ctx.TargetPlayerId, ctx.CombinedNumberValue);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
    }

    public static void Resolve(OperatorGameContext context, string targetPlayerId, decimal value)
    {
        if (context.GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            target.CurrentPoints += value;
            target.ScoreTimestamp = DateTimeOffset.UtcNow;
        }
    }
}
