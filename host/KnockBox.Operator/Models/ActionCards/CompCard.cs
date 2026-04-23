using KnockBox.Core.Primitives.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class CompCard() : ActionCard(CardAction.Comp)
{
    public override string CardIcon() => "\u2696\ufe0f";
    public override string TooltipName() => "Comp";
    public override string TooltipDescription() => "Changes your operator. Positive scores get Subtract, negative scores get Add. Blocked by Audit.";

    public override bool IsOperatorOnlyAction => true;

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new TargetedActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => !thisPlayer.IsAudited;

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        // Comp always resolves, even when action is blocked
        Resolve(ctx.GameContext, ctx.ThisPlayer.UserId);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context, string playerId)
    {
        if (context.GamePlayers.TryGetValue(playerId, out var player) && !player.IsAudited)
        {
            if (player.CurrentPoints < 0) player.ActiveOperator = CardOperator.Add;
            else if (player.CurrentPoints > 0) player.ActiveOperator = CardOperator.Subtract;
        }
    }
}
