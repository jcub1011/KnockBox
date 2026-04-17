using KnockBox.Core.Primitives.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class StealCard() : ActionCard(CardAction.Steal), ITargetableCard
{
    public override string CardIcon() => "\ud83d\udd75\ufe0f";
    public override string TooltipName() => "Steal";
    public override string TooltipDescription() => "Take a random card from a target player's hand and add it to yours.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new TargetedActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => p.Hand.Count > 0 && p != thisPlayer);
    }

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => GetPotentialTargets(context, thisPlayer).Any();

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.ActionBlocked || ctx.TargetPlayerId == null)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        if (ctx.GameContext.GamePlayers.TryGetValue(ctx.TargetPlayerId, out var target))
            target.IsBeingStolenFrom = true;
        Resolve(ctx.GameContext, ctx.ThisPlayer.UserId, ctx.TargetPlayerId);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context, string sourcePlayerId, string targetPlayerId)
    {
        if (context.GamePlayers.TryGetValue(sourcePlayerId, out var source) && context.GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            if (target.Hand.Count > 0)
            {
                var cardIdx = context.Rng.GetRandomInt(target.Hand.Count);
                var stolen = target.Hand[cardIdx];
                target.Hand.RemoveAt(cardIdx);
                source.Hand.Add(stolen);
            }
        }
    }
}
