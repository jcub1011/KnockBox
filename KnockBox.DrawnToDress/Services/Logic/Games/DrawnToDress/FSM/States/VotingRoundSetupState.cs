using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
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

            context.Logger.LogInformation(
                "FSM → VotingRoundSetupState. Round {n} of {total}. {count} matchup(s) generated.",
                round.RoundNumber, context.Config.VotingRounds, round.Matchups.Count);

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
                ? SwissTournamentService.CalculateWins(
                    context.State.VotingRounds,
                    context.State.Votes.Values)
                : new Dictionary<string, int>();

            return SwissTournamentService.GenerateRound(
                roundNumber,
                entrantIds,
                context.State.VotingRounds,
                wins);
        }
    }
}
