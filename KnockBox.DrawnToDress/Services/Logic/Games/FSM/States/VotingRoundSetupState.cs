using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Prepares the next Swiss voting round by generating head-to-head matchups and then
    /// immediately chains to <see cref="VotingMatchupState"/>.
    ///
    /// Transition ownership:
    /// - <see cref="OnEnter"/> generates the matchup list and chains to
    ///   <see cref="VotingMatchupState"/> without waiting for player input.
    /// </summary>
    public sealed class VotingRoundSetupState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.VotingRoundSetup);
            context.State.CurrentVotingRoundIndex = context.State.VotingRounds.Count;
            context.ResetReadyFlags();

            var round = BuildRound(context);
            context.State.VotingRounds.Add(round);

            int totalRounds = SwissTournamentService.ResolveRoundCount(
                context.GetTournamentEntrantIds().Count, context.Config.VotingRounds);
            context.Logger.LogInformation(
                "FSM → VotingRoundSetupState. Round {n} of {total}. {count} matchup(s), {byes} bye(s) generated.",
                round.RoundNumber, totalRounds, round.Matchups.Count, round.Byes.Count);

            // Chain immediately into the voting matchup.
            return new VotingMatchupState();
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command) => null;

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a Swiss-system pairing for the current round using
        /// <see cref="SwissTournamentService"/>.
        ///
        /// Round 1 pairs entrants by ID order (deterministic seed).
        /// Subsequent rounds group entrants by cumulative wins and avoid rematches.
        /// Only players who have submitted at least one outfit are included as entrants.
        /// </summary>
        private static VotingRound BuildRound(DrawnToDressGameContext context)
        {
            int roundNumber = context.State.VotingRounds.Count + 1;
            var entrantIds = context.GetTournamentEntrantIds();

            var wins = roundNumber > 1
                ? DrawnToDressScoringService.CalculateMatchupWins(
                    context.State.VotingRounds,
                    context.Config.VotingCriteria,
                    context.State.Votes.Values,
                    context.State.CriterionCoinFlipResults)
                : new Dictionary<EntrantId, double>();

            return SwissTournamentService.GenerateRound(
                roundNumber,
                entrantIds,
                context.State.VotingRounds,
                wins);
        }
    }
}
