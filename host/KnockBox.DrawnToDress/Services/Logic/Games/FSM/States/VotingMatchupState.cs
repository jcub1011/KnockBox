using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Timed state in which players cast votes for each head-to-head matchup.
    ///
    /// Transition ownership:
    /// - Timer expiry → check for ties → <see cref="CoinFlipState"/> or
    ///   <see cref="VotingRoundResultsState"/>
    /// - All votes cast early → same tie-check → next state
    /// - <see cref="CastVoteCommand"/> → vote recorded; may trigger early advance
    /// - <see cref="RequestCoinFlipCommand"/> → <see cref="CoinFlipState"/>
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// </summary>
    public sealed class VotingMatchupState : ITimedDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            if (context.Config.EnableTimer)
            {
                context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.VotingTimeSec);
            }

            context.State.SetPhase(GamePhase.Voting);
            context.ResetReadyFlags();
            context.Logger.LogDebug(
                "FSM → VotingMatchupState. Round {n}. Deadline: {deadline}.",
                context.State.CurrentVotingRoundIndex + 1, context.State.PhaseDeadlineUtc);
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
                case CastVoteCommand cmd:
                    return HandleCastVote(context, cmd);

                case RequestCoinFlipCommand cmd:
                    return HandleRequestCoinFlip(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);


                default:
                    context.Logger.LogWarning(
                        "VotingMatchupState: unrecognized command [{type}] from player [{id}].",
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

            context.Logger.LogDebug("Voting timer expired.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCastVote(
            DrawnToDressGameContext context, CastVoteCommand cmd)
        {
            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null)
            {
                context.Logger.LogWarning(
                    "CastVote: unknown voter [{id}].", cmd.PlayerId);
                return null;
            }

            // Enforce creator-voting exclusion: participants in the matchup may not vote on it.
            int roundIndex = context.State.CurrentVotingRoundIndex;
            if (roundIndex >= context.State.VotingRounds.Count)
            {
                context.Logger.LogWarning("CastVote: no active voting round.");
                return null;
            }

            var round = context.State.VotingRounds[roundIndex];
            var matchup = round.Matchups.FirstOrDefault(m => m.Id == cmd.MatchupId);
            if (matchup is null)
            {
                context.Logger.LogWarning(
                    "CastVote: matchup [{id}] not found in current round.", cmd.MatchupId);
                return null;
            }

            if (!VotingEligibilityService.IsEligibleToVote(cmd.PlayerId, matchup))
            {
                context.Logger.LogWarning(
                    "CastVote: player [{id}] is a participant in matchup [{matchupId}] and is not eligible to vote on it.",
                    cmd.PlayerId, cmd.MatchupId);
                return null;
            }

            // Validate that the criterion is known.
            if (!context.Config.VotingCriteria.Any(c => c.Id == cmd.CriterionId))
            {
                context.Logger.LogWarning(
                    "CastVote: unknown criterion [{id}].", cmd.CriterionId);
                return null;
            }

            // Validate that the chosen entrant is a participant in this matchup.
            if (cmd.ChosenEntrantId != matchup.EntrantAId && cmd.ChosenEntrantId != matchup.EntrantBId)
            {
                context.Logger.LogWarning(
                    "CastVote: chosen entrant [{id}] is not a participant in matchup [{matchupId}].",
                    cmd.ChosenEntrantId, cmd.MatchupId);
                return null;
            }

            // Override any previous vote from this voter on the same matchup+criterion.
            var existingKey = context.State.Votes
                .FirstOrDefault(kv =>
                    kv.Value.VoterPlayerId == cmd.PlayerId &&
                    kv.Value.MatchupId == cmd.MatchupId &&
                    kv.Value.CriterionId == cmd.CriterionId)
                .Key;
            if (existingKey != Guid.Empty)
                context.State.Votes.TryRemove(existingKey, out _);

            bool isLate = context.State.PhaseDeadlineUtc is { } voteDeadline && DateTimeOffset.UtcNow > voteDeadline;
            var submission = new VoteSubmission
            {
                VoterPlayerId = cmd.PlayerId,
                MatchupId = cmd.MatchupId,
                CriterionId = cmd.CriterionId,
                ChosenEntrantId = cmd.ChosenEntrantId,
                SubmittedAt = DateTimeOffset.UtcNow,
                IsLate = isLate,
            };
            context.State.Votes[Guid.NewGuid()] = submission;

            if (isLate)
                context.Logger.LogDebug(
                    "Player [{voter}] voted LATE for [{chosen}] in matchup [{matchup}] on criterion [{criterion}].",
                    cmd.PlayerId, cmd.ChosenPlayerId, cmd.MatchupId, cmd.CriterionId);
            else
                context.Logger.LogDebug(
                    "Player [{voter}] voted for [{chosen}] in matchup [{matchup}] on criterion [{criterion}].",
                    cmd.PlayerId, cmd.ChosenPlayerId, cmd.MatchupId, cmd.CriterionId);

            // Advance early when all expected votes for this round have been cast.
            if (AllVotesCast(context))
            {
                context.Logger.LogDebug("All votes cast. Advancing.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
            }

            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleRequestCoinFlip(
            DrawnToDressGameContext context, RequestCoinFlipCommand cmd)
        {
            context.State.PendingCoinFlipMatchupId = cmd.MatchupId;
            context.Logger.LogDebug(
                "Coin-flip requested for matchup [{matchupId}] by [{id}].",
                cmd.MatchupId, cmd.PlayerId);

            // Build a queue entry for the manually requested flip.
            int roundIndex = context.State.CurrentVotingRoundIndex;
            var round = roundIndex < context.State.VotingRounds.Count
                ? context.State.VotingRounds[roundIndex] : null;
            var matchup = round?.Matchups.FirstOrDefault(m => m.Id == cmd.MatchupId);

            if (matchup is not null)
            {
                context.State.PendingCoinFlipQueue =
                [
                    new PendingCoinFlipEntry
                    {
                        Context = CoinFlipContext.CriterionTie,
                        MatchupId = cmd.MatchupId,
                        EntrantAId = matchup.EntrantAId,
                        EntrantBId = matchup.EntrantBId,
                    }
                ];
            }

            return new CoinFlipState(new VotingRoundResultsState());
        }

        private static IGameState<DrawnToDressGameContext, DrawnToDressCommand> ChooseNextState(DrawnToDressGameContext context)
        {
            int roundIndex = context.State.CurrentVotingRoundIndex;
            if (roundIndex < context.State.VotingRounds.Count)
            {
                var round = context.State.VotingRounds[roundIndex];
                var tiedCriteria = DrawnToDressScoringService.FindTiedCriteria(
                    round,
                    context.Config.VotingCriteria,
                    context.State.Votes.Values,
                    context.State.CriterionCoinFlipResults);

                if (tiedCriteria.Count > 0)
                {
                    context.State.PendingCoinFlips = tiedCriteria;

                    // Build interactive coin flip queue entries.
                    var queue = new List<PendingCoinFlipEntry>();
                    foreach (var (matchupId, criterionId) in tiedCriteria)
                    {
                        var matchup = round.Matchups.FirstOrDefault(m => m.Id == matchupId);
                        if (matchup is null) continue;

                        queue.Add(new PendingCoinFlipEntry
                        {
                            Context = CoinFlipContext.CriterionTie,
                            MatchupId = matchupId,
                            CriterionId = criterionId,
                            EntrantAId = matchup.EntrantAId,
                            EntrantBId = matchup.EntrantBId,
                        });
                    }

                    context.State.PendingCoinFlipQueue = queue;

                    context.Logger.LogDebug(
                        "Found {count} tied criteria in round {round}. Moving to coin flip.",
                        tiedCriteria.Count, roundIndex + 1);
                    return new CoinFlipState(new VotingRoundResultsState());
                }
            }

            return new VotingRoundResultsState();
        }

        /// <summary>
        /// Returns <see langword="true"/> when every eligible voter has cast a vote for
        /// every configured criterion on every matchup in the current round.
        ///
        /// A player is eligible to vote on a matchup when they are not a participant in it
        /// (see <see cref="VotingEligibilityService.IsEligibleToVote"/>).  If a matchup has
        /// no eligible voters (e.g. a two-player game) it is treated as already complete.
        /// </summary>
        private static bool AllVotesCast(DrawnToDressGameContext context)
        {
            int roundIndex = context.State.CurrentVotingRoundIndex;
            if (roundIndex >= context.State.VotingRounds.Count) return false;

            var round = context.State.VotingRounds[roundIndex];
            if (round.Matchups.Count == 0) return true;

            var criteriaIds = context.Config.VotingCriteria.Select(c => c.Id).ToList();
            if (criteriaIds.Count == 0) return true;

            var allPlayerIds = context.GamePlayers.Keys;

            // Build a lookup set of (voterId, matchupId, criterionId) triples already cast.
            var castTriples = context.State.Votes.Values
                .Where(v => round.Matchups.Any(m => m.Id == v.MatchupId))
                .Select(v => (v.VoterPlayerId, v.MatchupId, v.CriterionId))
                .ToHashSet();

            foreach (var matchup in round.Matchups)
            {
                var eligibleVoterIds = VotingEligibilityService.GetEligibleVoterIds(matchup, allPlayerIds);
                foreach (var voterId in eligibleVoterIds)
                {
                    foreach (var criterionId in criteriaIds)
                    {
                        if (!castTriples.Contains((voterId, matchup.Id, criterionId)))
                            return false;
                    }
                }
            }

            return true;
        }
    }
}
