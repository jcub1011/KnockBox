using KnockBox.Core.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class CookTheBooksCard()
    : ActionCard(CardAction.CookTheBooks), IPairableCard
{
    public override string CardIcon() => "\ud83e\uddd1\u200d\ud83c\udf73";
    public override string TooltipName() => "Cook the Books";
    public override string TooltipDescription() => "Play with a number card to divide your score by that number.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new TargetedActionCommand(context, playCommand, playedCards, this);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer) => [];

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
        if (ctx.ActionBlocked)
            return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
        Resolve(ctx.GameContext, ctx.ThisPlayer.UserId, ctx.CombinedNumberValue);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.OkConsumedNumbers());
    }

    public static void Resolve(OperatorGameContext context, string playerId, decimal incomingValue)
    {
        if (context.GamePlayers.TryGetValue(playerId, out var player))
        {
            var (newScore, newOp) = OperatorGameContext.CalculateNewScore(player.CurrentPoints, CardOperator.Divide, incomingValue);
            player.CurrentPoints = newScore;
            if (incomingValue == 0m) player.ActiveOperator = newOp;
            player.ScoreTimestamp = DateTimeOffset.UtcNow;
        }
    }
}
