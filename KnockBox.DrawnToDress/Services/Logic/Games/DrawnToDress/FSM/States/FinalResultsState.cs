using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Terminal state that displays the final leaderboard and game-over screen.
    ///
    /// No further gameplay transitions are possible from this state other than abandoning
    /// (e.g. to clean up resources on the server).
    ///
    /// Transition ownership:
    /// - <see cref="AbandonGameCommand"/> → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class FinalResultsState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Results);
            context.Logger.LogInformation("FSM → FinalResultsState. Game complete.");

            var players = context.GamePlayers.ToDictionary(
                kv => kv.Key, kv => kv.Value) as IReadOnlyDictionary<string, State.Games.DrawnToDress.Data.DrawnToDressPlayerState>;

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

            if (tiedPairs.Count > 0)
            {
                context.Logger.LogInformation(
                    "Leaderboard has {count} tied pair(s) that may need coin flip resolution (deferred to issue #63).",
                    tiedPairs.Count);
            }

            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            if (command is AbandonGameCommand)
                return new AbandonedState();

            return null;
        }
    }
}
