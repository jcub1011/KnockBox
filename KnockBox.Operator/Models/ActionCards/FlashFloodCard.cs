using KnockBox.Core.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class FlashFloodCard() : ActionCard(CardAction.FlashFlood)
{
    public override string CardIcon() => "\ud83c\udf0a";
    public override string TooltipName() => "Flash Flood";
    public override string TooltipDescription() => "All players draw 2 extra cards from the deck.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new GlobalActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => context.State.Deck.Count > 0;

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.ActionBlocked) return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        Resolve(ctx.GameContext);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context)
    {
        foreach (var player in context.GamePlayers.Values)
        {
            context.DealCards(player, 2);
        }
    }
}
