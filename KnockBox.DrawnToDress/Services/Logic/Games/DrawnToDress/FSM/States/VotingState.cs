using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Voting phase: players vote on paired outfit matchups according to the Swiss-system
    /// tournament. A countdown timer auto-finalizes each round when it expires.
    /// Auto-advances when all eligible voters have voted for all matchups.
    /// Transitions to <see cref="ResultsState"/> after the last round.
    /// </summary>
    public sealed class VotingState
        : IDrawnToDressGameState,
          ITimedGameState<DrawnToDressGameContext, DrawnToDressCommand>
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Voting);
            context.State.SetPhaseDeadline(
                DateTimeOffset.UtcNow.AddSeconds(context.Settings.VotingTimePerRound));
            context.Logger.LogInformation(
                "FSM → VotingState (round {r} / {total}, deadline: {dl})",
                context.State.CurrentVotingRound, context.State.TotalVotingRounds,
                context.State.PhaseDeadlineUtc);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.ClearPhaseDeadline();
            return Result.Success;
        }

        // ── ITimedGameState ───────────────────────────────────────────────────

        public ValueResult<TimeSpan> GetRemainingTime(DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (!context.State.PhaseDeadlineUtc.HasValue)
                return TimeSpan.Zero;
            var remaining = context.State.PhaseDeadlineUtc.Value - now;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (!context.State.PhaseDeadlineUtc.HasValue || now < context.State.PhaseDeadlineUtc.Value)
                return null;

            context.Logger.LogInformation(
                "VotingState: timer expired for round {r}, auto-finalizing.",
                context.State.CurrentVotingRound);

            return DoFinalizeRound(context);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            return command switch
            {
                CastVoteCommand cmd => HandleCastVote(context, cmd),
                FinalizeVotingRoundCommand cmd => HandleFinalize(context, cmd),
                _ => null
            };
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCastVote(
            DrawnToDressGameContext context, CastVoteCommand cmd)
        {
            var matchup = context.State.VotingMatchups.FirstOrDefault(m => m.Id == cmd.MatchupId);
            if (matchup is null)
                return new ResultError("Matchup not found.");

            if (matchup.IsComplete)
                return new ResultError("Voting for this matchup has already closed.");

            var outfitA = context.State.Outfits.GetValueOrDefault(matchup.OutfitAId);
            var outfitB = context.State.Outfits.GetValueOrDefault(matchup.OutfitBId);

            if (outfitA?.PlayerId == cmd.PlayerId || outfitB?.PlayerId == cmd.PlayerId)
                return new ResultError("You cannot vote on an outfit you created.");

            if (matchup.VotedPlayerIds.Contains(cmd.PlayerId))
                return new ResultError("You have already voted on this matchup.");

            foreach (var criterion in context.Settings.VotingCriteria)
            {
                if (!cmd.Votes.TryGetValue(criterion, out bool voteForA))
                    return new ResultError($"Missing vote for criterion '{criterion}'.");

                if (!matchup.CriterionVotes.ContainsKey(criterion))
                    matchup.CriterionVotes[criterion] = (0, 0);

                var (a, b) = matchup.CriterionVotes[criterion];
                matchup.CriterionVotes[criterion] = voteForA ? (a + 1, b) : (a, b + 1);
            }

            matchup.VotedPlayerIds.Add(cmd.PlayerId);

            // Auto-advance if all eligible players have voted for all current-round matchups
            if (AllEligiblePlayersVotedForCurrentRound(context))
            {
                context.Logger.LogInformation(
                    "VotingState: all eligible players voted in round {r} — auto-finalizing.",
                    context.State.CurrentVotingRound);
                return DoFinalizeRound(context);
            }

            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleFinalize(
            DrawnToDressGameContext context, FinalizeVotingRoundCommand cmd)
        {
            if (!context.IsHost(cmd.PlayerId))
                return new ResultError("Only the host can finalize a voting round.");

            return DoFinalizeRound(context);
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> DoFinalizeRound(
            DrawnToDressGameContext context)
        {
            context.FinalizeCurrentRoundMatchups();

            if (context.State.CurrentVotingRound >= context.State.TotalVotingRounds)
            {
                context.AwardTournamentBonus();
                context.Logger.LogInformation("VotingState: tournament complete, transitioning to Results.");
                return new ResultsState();
            }

            context.State.AdvanceVotingRound();
            context.GenerateSwissPairings();
            // Reset the per-round deadline for the new voting round
            context.State.SetPhaseDeadline(
                DateTimeOffset.UtcNow.AddSeconds(context.Settings.VotingTimePerRound));
            context.Logger.LogInformation(
                "VotingState: advanced to round {r} / {total}, new deadline {dl}.",
                context.State.CurrentVotingRound, context.State.TotalVotingRounds,
                context.State.PhaseDeadlineUtc);

            // Stay in VotingState for the next round
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when every eligible voter (those who are not a creator of either outfit)
        /// has cast a vote for every non-complete matchup in the current voting round.
        /// </summary>
        private static bool AllEligiblePlayersVotedForCurrentRound(DrawnToDressGameContext context)
        {
            var currentMatchups = context.State.CurrentRoundMatchups.Where(m => !m.IsComplete).ToList();
            if (currentMatchups.Count == 0) return false;

            foreach (var matchup in currentMatchups)
            {
                var outfitA = context.State.Outfits.GetValueOrDefault(matchup.OutfitAId);
                var outfitB = context.State.Outfits.GetValueOrDefault(matchup.OutfitBId);

                var eligible = context.AllParticipants
                    .Where(p => p.Id != outfitA?.PlayerId && p.Id != outfitB?.PlayerId)
                    .ToList();

                if (eligible.Any(p => !matchup.VotedPlayerIds.Contains(p.Id)))
                    return false;
            }
            return true;
        }
    }
}
