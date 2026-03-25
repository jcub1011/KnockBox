using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
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
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class VotingMatchupState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.VotingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.Voting);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → VotingMatchupState. Round {n}. Deadline: {deadline}.",
                context.State.CurrentVotingRoundIndex + 1, _deadline);
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

            context.Logger.LogInformation("Voting timer expired.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCastVote(
            DrawnToDressGameContext context, CastVoteCommand cmd)
        {
            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null)
            {
                context.Logger.LogWarning(
                    "CastVote: unknown voter [{id}].", cmd.PlayerId);
                return null;
            }

            var submission = new VoteSubmission
            {
                VoterPlayerId = cmd.PlayerId,
                MatchupId = cmd.MatchupId,
                CriterionId = cmd.CriterionId,
                ChosenPlayerId = cmd.ChosenPlayerId,
                SubmittedAt = DateTimeOffset.UtcNow,
            };
            context.State.Votes[Guid.NewGuid()] = submission;

            context.Logger.LogInformation(
                "Player [{voter}] voted for [{chosen}] in matchup [{matchup}] on criterion [{criterion}].",
                cmd.PlayerId, cmd.ChosenPlayerId, cmd.MatchupId, cmd.CriterionId);

            // Advance early when all expected votes for this round have been cast.
            if (AllVotesCast(context))
            {
                context.Logger.LogInformation("All votes cast. Advancing.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
            }

            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleRequestCoinFlip(
            DrawnToDressGameContext context, RequestCoinFlipCommand cmd)
        {
            context.State.PendingCoinFlipMatchupId = cmd.MatchupId;
            context.Logger.LogInformation(
                "Coin-flip requested for matchup [{matchupId}] by [{id}].",
                cmd.MatchupId, cmd.PlayerId);
            return new CoinFlipState();
        }

        private static IGameState<DrawnToDressGameContext, DrawnToDressCommand> ChooseNextState(DrawnToDressGameContext context)
        {
            // TODO: Check vote tallies for ties and return CoinFlipState if needed.
            // Placeholder: always proceed to results for now.
            return new VotingRoundResultsState();
        }

        /// <summary>
        /// Returns <see langword="true"/> when every voter-matchup-criterion combination
        /// for the current round has been submitted.
        ///
        /// TODO: Replace with proper expected-vote-count logic in a later issue.
        /// Placeholder: mark ready when every player has submitted at least one vote.
        /// </summary>
        private static bool AllVotesCast(DrawnToDressGameContext context)
        {
            int roundIndex = context.State.CurrentVotingRoundIndex;
            if (roundIndex >= context.State.VotingRounds.Count) return false;

            var round = context.State.VotingRounds[roundIndex];
            var votersInRound = new HashSet<string>(
                context.State.Votes.Values
                    .Where(v => round.Matchups.Any(m => m.Id == v.MatchupId))
                    .Select(v => v.VoterPlayerId));

            return context.GamePlayers.Keys.All(id => votersInRound.Contains(id));
        }
    }
}
