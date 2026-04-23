using KnockBox.Core.Primitives.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class MarketCrashCard() : ActionCard(CardAction.MarketCrash)
{
    public override string CardIcon() => "\ud83d\udcc9";
    public override string TooltipName() => "Market Crash";
    public override string TooltipDescription() => "Forces ALL players to switch to the Divide operator. Audited players are unaffected.";

    public override bool IsOperatorOnlyAction => true;

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new GlobalActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => true;

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        // MarketCrash always resolves, even when action is blocked
        Resolve(ctx.GameContext);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context)
    {
        foreach (var player in context.GamePlayers.Values)
        {
            if (!player.IsAudited) player.ActiveOperator = CardOperator.Divide;
        }
    }
}
