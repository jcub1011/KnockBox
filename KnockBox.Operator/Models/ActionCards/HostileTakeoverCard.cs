using KnockBox.Core.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class HostileTakeoverCard() : ActionCard(CardAction.HostileTakeover), ITargetableCard
{
    public override string CardIcon() => "\ud83e\udd1d";
    public override string TooltipName() => "Hostile Takeover";
    public override string TooltipDescription() => "Swap your active operator with a target player's. Blocked by Audit.";

    public override bool IsOperatorOnlyAction => true;

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new TargetedActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public IEnumerable<OperatorPlayerState> GetPotentialTargets(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return context.GamePlayers.Values.Where(p => !p.IsAudited && p != thisPlayer);
    }

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => GetPotentialTargets(context, thisPlayer).Any();

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.ActionBlocked || ctx.TargetPlayerId == null)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        Resolve(ctx.GameContext, ctx.ThisPlayer.UserId, ctx.TargetPlayerId);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context, string sourcePlayerId, string targetPlayerId)
    {
        if (context.GamePlayers.TryGetValue(sourcePlayerId, out var source) && context.GamePlayers.TryGetValue(targetPlayerId, out var target))
        {
            if (!source.IsAudited && !target.IsAudited)
            {
                (target.ActiveOperator, source.ActiveOperator) = (source.ActiveOperator, target.ActiveOperator);
            }
        }
    }
}
