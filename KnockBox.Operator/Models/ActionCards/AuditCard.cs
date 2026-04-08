using KnockBox.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class AuditCard() : ActionCard(CardAction.Audit), ITargetableCard
{
    public override string CardIcon() => "\ud83d\udcdd";
    public override string TooltipName() => "Audit";
    public override string TooltipDescription() => "Locks a target player's operator for a full round. They cannot change it until the audit expires.";

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => !p.IsAudited);
    }

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => GetPotentialTargets(context, thisPlayer).Any();

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.ActionBlocked || ctx.TargetPlayerId == null)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        Resolve(ctx.GameContext, ctx.TargetPlayerId);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context, string targetPlayerId)
    {
        if (context.GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            target.IsAudited = true;
            target.AuditExpiresTurnCount = context.State.TurnCount + context.GamePlayers.Count;
        }
    }
}
