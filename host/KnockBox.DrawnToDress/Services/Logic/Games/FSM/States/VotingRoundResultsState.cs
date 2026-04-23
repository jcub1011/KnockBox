using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
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
    /// </summary>
    public sealed class VotingRoundResultsState : ITimedDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.VotingRoundResults);
            context.ResetReadyFlags();

            context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.VotingRoundResultsTimeSec);

            int totalRounds = SwissTournamentService.ResolveRoundCount(
                context.GetTournamentEntrantIds().Count, context.Config.VotingRounds);
            context.Logger.LogDebug(
                "FSM → VotingRoundResultsState. Round {n} of {total} complete. Auto-advance in {sec}s.",
                context.State.CurrentVotingRoundIndex + 1, totalRounds,
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
                    var playerId = entrantId.PlayerId;
                    var player = context.GetPlayer(playerId);
                    if (player is not null)
                    {
                        player.BonusPoints += context.Config.RoundLeaderBonusPoints;
                        context.Logger.LogDebug(
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

                default:
                    context.Logger.LogWarning(
                        "VotingRoundResultsState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => context.State.PhaseDeadlineUtc is { } deadline
                ? deadline - now
                : new ResultError("No timer active.");

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (context.State.PhaseDeadlineUtc is not { } deadline || now < deadline) return null;

            context.Logger.LogDebug("Voting round results timer expired. Advancing.");
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
                context.Logger.LogDebug("Advancing to next voting round.");
                return new VotingRoundSetupState();
            }

            context.Logger.LogDebug("All voting rounds complete. Moving to final results.");
            return new FinalResultsState();
        }
    }
}
