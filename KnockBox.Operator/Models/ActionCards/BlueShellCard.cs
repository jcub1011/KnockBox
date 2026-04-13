using KnockBox.Extensions.Returns;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Models;

public sealed class BlueShellCard() : ActionCard(CardAction.BlueShell)
{
    public override string CardIcon() => "\ud83d\udc22";
    public override string TooltipName() => "Blue Shell";
    public override string TooltipDescription() => "Resets ALL players with a score of 0.0 to 10.0 and sets their operator to (+). Only playable if someone is at 0.0. Each affected player can block this individually.";

    public override IGameActionCommand CreateCommand(OperatorGameContext context, PlayCardsCommand playCommand, List<Card> playedCards)
        => new BlueShellCommand(context, playCommand, playedCards);

    public override IEnumerable<Card> GetPotentialReactionCards(OperatorGameContext context, OperatorPlayerState thisPlayer)
    {
        return thisPlayer.Hand.Where(c => c is ActionCard { ActionValue: CardAction.Shield });
    }

    public override bool IsPlayable(OperatorGameContext context, OperatorPlayerState thisPlayer)
        => context.GamePlayers.Values.Any(p => p.CurrentPoints == 0m);

    public override ValueResult<CardPlayResult> Play(CardPlayContext ctx)
    {
        if (ctx.ActionBlocked) return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
        var blockedPlayerIds = ctx.GameContext.State.PlayerReactions
            .Where(r => r.ReactionCard != null)
            .Select(r => r.PlayerId)
            .ToHashSet();
        Resolve(ctx.GameContext, blockedPlayerIds);
        return ValueResult<CardPlayResult>.FromValue(CardPlayResult.Ok());
    }

    public static void Resolve(OperatorGameContext context, HashSet<string>? blockedPlayerIds = null)
    {
        foreach (var player in context.GamePlayers.Values)
        {
            if (player.CurrentPoints == 0m && (blockedPlayerIds == null || !blockedPlayerIds.Contains(player.UserId)))
            {
                player.CurrentPoints = 10.0m;
                player.ActiveOperator = CardOperator.Add;
                player.ScoreTimestamp = DateTimeOffset.UtcNow;
            }
        }
    }
}
