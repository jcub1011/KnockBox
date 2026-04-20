using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public class StandardPlayCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards)
    : BaseActionCommand(context, playCommand, playedCards)
{
    public override void Execute()
    {
        if (!Context.GamePlayers.TryGetValue(PlayCommand.PlayerId, out var pState))
            return;

        LogPlay(false);

        var numbers = PlayedCards.OfType<NumberCard>().ToList();
        var opCard = PlayedCards.OfType<OperatorCard>().LastOrDefault();
        var val = CalculateNumberValue();

        var playContext = new CardPlayContext(
            GameContext: Context,
            ThisPlayer: pState,
            TargetPlayerId: PlayCommand.TargetPlayerId,
            CombinedNumberValue: val,
            PairedNumbers: numbers,
            ActionBlocked: false
        );

        // Standard number score logic
        if (numbers.Count > 0)
        {
            ResolveNumberScoring(pState, val);
        }

        // Standard operator card logic
        if (opCard != null)
        {
            var opResult = opCard.Play(playContext);
            if (opResult.TryGetSuccess(out var opPlayResult) && opPlayResult.Toggled && opPlayResult.OperatorTargetId != null)
            {
                if (Context.GamePlayers.TryGetValue(opPlayResult.OperatorTargetId, out var opTarget))
                {
                    string targetNameLog = GetPlayerName(opTarget.UserId);
                    Context.State.ActionLog.Add(new ActionLogEntry(
                        $"Operator toggled to opposite! {targetNameLog} is now {opTarget.ActiveOperator}.",
                        DateTimeOffset.UtcNow,
                        null,
                        opTarget.UserId));
                }
            }
        }
    }

    private void ResolveNumberScoring(OperatorPlayerState targetPlayerState, decimal val)
    {
        if (targetPlayerState.ActiveOperator == CardOperator.Divide)
        {
            targetPlayerState.DivideUses++;
            if (targetPlayerState.DivideUses >= 4 || val == 0m)
            {
                targetPlayerState.IsDivideBroken = true;
                targetPlayerState.ActiveOperator = CardOperator.Add;
                targetPlayerState.DivideUses = 0;

                string targetPlayerName = GetPlayerName(targetPlayerState.UserId);
                Context.State.ActionLog.Add(new ActionLogEntry(
                    $"The Divide operator shattered! {targetPlayerName}'s operator reverted to Plus.",
                    DateTimeOffset.UtcNow,
                    null,
                    targetPlayerState.UserId));

                if (val == 0m)
                {
                    targetPlayerState.CurrentPoints = 0m;
                }
                else
                {
                    var (newScore, _) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, CardOperator.Divide, val);
                    targetPlayerState.CurrentPoints = newScore;
                }
            }
            else
            {
                var (newScore, newOp) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, targetPlayerState.ActiveOperator, val);
                targetPlayerState.CurrentPoints = newScore;
                targetPlayerState.ActiveOperator = newOp;
            }
        }
        else
        {
            var (newScore, newOp) = OperatorGameContext.CalculateNewScore(targetPlayerState.CurrentPoints, targetPlayerState.ActiveOperator, val);
            targetPlayerState.CurrentPoints = newScore;
            targetPlayerState.ActiveOperator = newOp;
        }

        targetPlayerState.ScoreTimestamp = DateTimeOffset.UtcNow;
    }
}
