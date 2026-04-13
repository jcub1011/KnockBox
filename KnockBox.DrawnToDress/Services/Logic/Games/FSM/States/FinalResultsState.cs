using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Computation state that calculates the final leaderboard and checks for tied pairs.
    ///
    /// If there are ties that cannot be broken by matchup wins, this state populates the
    /// <see cref="DrawnToDressGameState.PendingCoinFlipQueue"/> with
    /// <see cref="CoinFlipContext.FinalStandingsTie"/> entries and chains to
    /// <see cref="CoinFlipState"/> → <see cref="FinalResultsDisplayState"/>.
    ///
    /// If there are no ties (or all ties are broken by matchup wins), it chains directly
    /// to <see cref="FinalResultsDisplayState"/>.
    /// </summary>
    public sealed class FinalResultsState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.Logger.LogInformation("FSM → FinalResultsState. Computing leaderboard.");

            var players = context.GamePlayers.ToDictionary(
                kv => kv.Key, kv => kv.Value) as IReadOnlyDictionary<string, DrawnToDressPlayerState>;

            // 1. Calculate player totals (before tournament winner bonus).
            var playerTotals = DrawnToDressScoringService.CalculatePlayerTotals(
                context.State.VotingRounds,
                context.Config.VotingCriteria,
                context.State.Votes.Values,
                context.State.CriterionCoinFlipResults,
                players,
                context.Config);

            // 2. Award tournament winner bonus to highest-scoring player(s).
            if (context.Config.TournamentWinnerBonusPoints > 0 && playerTotals.Count > 0)
            {
                double maxScore = playerTotals.Values.Max();
                foreach (var (playerId, score) in playerTotals)
                {
                    if (score == maxScore)
                    {
                        var player = context.GetPlayer(playerId);
                        if (player is not null)
                        {
                            player.BonusPoints += context.Config.TournamentWinnerBonusPoints;
                            context.Logger.LogInformation(
                                "Tournament winner bonus (+{bonus}) awarded to player [{playerId}].",
                                context.Config.TournamentWinnerBonusPoints, playerId);
                        }
                    }
                }
            }

            // 3. Build leaderboard (includes the tournament winner bonus in totals).
            var (entries, tiedPairs) = DrawnToDressScoringService.BuildLeaderboard(
                context.State.VotingRounds,
                context.Config.VotingCriteria,
                context.State.Votes.Values,
                context.State.CriterionCoinFlipResults,
                players,
                context.Config);

            // 4. Store in state.
            context.State.Leaderboard = entries;

            // 5. If tied pairs exist, resolve via coin flip.
            if (tiedPairs.Count > 0)
            {
                context.Logger.LogInformation(
                    "Leaderboard has {count} tied pair(s). Setting up coin flip resolution.",
                    tiedPairs.Count);

                var queue = new List<PendingCoinFlipEntry>();
                foreach (var (playerA, playerB) in tiedPairs)
                {
                    queue.Add(new PendingCoinFlipEntry
                    {
                        Context = CoinFlipContext.FinalStandingsTie,
                        PlayerAId = playerA,
                        PlayerBId = playerB,
                    });
                }

                context.State.PendingCoinFlipQueue = queue;
                return new CoinFlipState(new FinalResultsDisplayState());
            }

            // No ties — go straight to display.
            return new FinalResultsDisplayState();
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            return null;
        }
    }
}
