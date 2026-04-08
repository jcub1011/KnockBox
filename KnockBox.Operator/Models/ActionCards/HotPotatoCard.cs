using KnockBox.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class HotPotatoCard()
    : ActionCard(CardAction.HotPotato), ITargetableCard, IPairableCard
{
    public override string CardIcon() => "\ud83e\udd54";
    public override string TooltipName() => "Hot Potato";
    public override string TooltipDescription() => "Play with number cards to apply it to the target player's score using their operator. They can redirect it with their own Hot Potato.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield or CardAction.HotPotato });
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
        Resolve(ctx.GameContext, ctx.TargetPlayerId, ctx.CombinedNumberValue);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
    }

    public static void Resolve(OperatorGameContext context, string targetPlayerId, decimal value)
    {
        if (context.GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            var (newScore, newOp) = OperatorGameContext.CalculateNewScore(target.CurrentPoints, target.ActiveOperator, value);
            target.CurrentPoints = newScore;
            target.ActiveOperator = newOp;
            target.ScoreTimestamp = DateTimeOffset.UtcNow;
        }
    }
}
