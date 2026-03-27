using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed display state that shows the results of a single Swiss voting round.
    /// Auto-advances after <see cref="Data.DrawnToDressConfig.VotingRoundResultsTimeSec"/>
    /// seconds to the next round or final results. No player interaction is required.
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="VotingRoundSetupState"/> (next round) or
    ///   <see cref="FinalResultsState"/> (last round)
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class VotingRoundResultsState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.VotingRoundResults);
            context.ResetReadyFlags();

            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.VotingRoundResultsTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;

            context.Logger.LogInformation(
                "FSM → VotingRoundResultsState. Round {n} of {total} complete. Auto-advance in {sec}s.",
                context.State.CurrentVotingRoundIndex + 1, context.Config.VotingRounds,
                context.Config.VotingRoundResultsTimeSec);

            // Compute round scores and award round leader bonus.
            int roundIndex = context.State.CurrentVotingRoundIndex;
            if (roundIndex < context.State.VotingRounds.Count && context.Config.RoundLeaderBonusPoints > 0)
            {
                var round = context.State.VotingRounds[roundIndex];
                var roundScores = DrawnToDressScoringService.CalculateRoundScores(
                    round,
                    context.Config.VotingCriteria,
                    context.State.Votes.Values,
                    context.State.CriterionCoinFlipResults);

                var leaders = DrawnToDressScoringService.GetRoundLeaders(roundScores);
                foreach (var entrantId in leaders)
                {
                    var playerId = DrawnToDressGameContext.GetPlayerIdFromEntrantId(entrantId);
                    var player = context.GetPlayer(playerId);
                    if (player is not null)
                    {
                        player.BonusPoints += context.Config.RoundLeaderBonusPoints;
                        context.Logger.LogInformation(
                            "Round leader bonus (+{bonus}) awarded to player [{playerId}] via entrant [{entrantId}].",
                            context.Config.RoundLeaderBonusPoints, playerId, entrantId);
                    }
                }
            }

            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => _deadline - now;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (now < _deadline) return null;

            context.Logger.LogInformation("Voting round results timer expired. Advancing.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>
                .FromValue(ChooseNextState(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static IGameState<DrawnToDressGameContext, DrawnToDressCommand> ChooseNextState(
            DrawnToDressGameContext context)
        {
            int entrantCount = context.GetTournamentEntrantIds().Count;
            int totalRounds = SwissTournamentService.ResolveRoundCount(entrantCount, context.Config.VotingRounds);
            bool moreRounds = context.State.VotingRounds.Count < totalRounds;
            if (moreRounds)
            {
                context.Logger.LogInformation("Advancing to next voting round.");
                return new VotingRoundSetupState();
            }

            context.Logger.LogInformation("All voting rounds complete. Moving to final results.");
            return new FinalResultsState();
        }
    }
}
