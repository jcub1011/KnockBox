using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Voting phase: players vote on paired outfit matchups according to the Swiss-system
    /// tournament. The host finalizes each round; the FSM transitions to
    /// <see cref="ResultsState"/> after the last round.
    /// </summary>
    public sealed class VotingState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Voting);
            context.Logger.LogInformation(
                "FSM → VotingState (round {r} / {total})",
                context.State.CurrentVotingRound, context.State.TotalVotingRounds);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

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
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleFinalize(
            DrawnToDressGameContext context, FinalizeVotingRoundCommand cmd)
        {
            if (!context.IsHost(cmd.PlayerId))
                return new ResultError("Only the host can finalize a voting round.");

            context.FinalizeCurrentRoundMatchups();

            if (context.State.CurrentVotingRound >= context.State.TotalVotingRounds)
            {
                context.AwardTournamentBonus();
                context.Logger.LogInformation("VotingState: tournament complete, transitioning to Results.");
                return new ResultsState();
            }

            context.State.AdvanceVotingRound();
            context.GenerateSwissPairings();
            context.Logger.LogInformation(
                "VotingState: advanced to round {r} / {total}.",
                context.State.CurrentVotingRound, context.State.TotalVotingRounds);

            // Stay in VotingState for the next round
            return null;
        }
    }
}
