using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Resolves all tied voting criteria via random coin flips and immediately chains to
    /// <see cref="VotingRoundResultsState"/>.
    ///
    /// Processes all entries in <see cref="DrawnToDressGameState.PendingCoinFlips"/>,
    /// recording a <see cref="CriterionCoinFlipResult"/> for each.
    ///
    /// Transition ownership:
    /// - <see cref="OnEnter"/> performs all flips and chains to
    ///   <see cref="VotingRoundResultsState"/> without waiting for player input.
    /// </summary>
    public sealed class CoinFlipState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.CoinFlip);
            context.Logger.LogInformation(
                "FSM → CoinFlipState. Resolving {count} pending coin flips.",
                context.State.PendingCoinFlips.Count);

            int roundIndex = context.State.CurrentVotingRoundIndex;
            var round = roundIndex < context.State.VotingRounds.Count
                ? context.State.VotingRounds[roundIndex]
                : null;

            foreach (var (matchupId, criterionId) in context.State.PendingCoinFlips)
            {
                var matchup = round?.Matchups.FirstOrDefault(m => m.Id == matchupId);
                if (matchup is null)
                {
                    context.Logger.LogWarning(
                        "CoinFlip: matchup [{matchupId}] not found. Skipping.", matchupId);
                    continue;
                }

                bool isHeads = Random.Shared.Next(2) == 0;
                string winner = isHeads ? matchup.EntrantAId : matchup.EntrantBId;

                context.State.CriterionCoinFlipResults.Add(
                    new CriterionCoinFlipResult(matchupId, criterionId, winner));

                context.Logger.LogInformation(
                    "Coin flip for matchup [{matchupId}] criterion [{criterionId}]: {result} → winner [{winner}].",
                    matchupId, criterionId, isHeads ? "Heads" : "Tails", winner);
            }

            context.State.PendingCoinFlips.Clear();

            return new VotingRoundResultsState();
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PendingCoinFlipMatchupId = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command) => null;
    }
}
