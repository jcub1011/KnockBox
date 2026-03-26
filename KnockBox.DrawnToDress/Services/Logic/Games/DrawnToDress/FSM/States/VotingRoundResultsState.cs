using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Displays the results of a single Swiss voting round. After all players
    /// acknowledge (or a timer fires in a later issue), the engine advances to the
    /// next round or ends the game.
    ///
    /// Transition ownership:
    /// - All players mark ready → <see cref="VotingRoundSetupState"/> (next round) or
    ///   <see cref="FinalResultsState"/> (last round)
    /// - <see cref="MarkReadyCommand"/> → recorded; may trigger advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class VotingRoundResultsState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.VotingRoundResults);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → VotingRoundResultsState. Round {n} of {total} complete.",
                context.State.CurrentVotingRoundIndex + 1, context.Config.VotingRounds);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case MarkReadyCommand cmd:
                    return HandleMarkReady(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleMarkReady(
            DrawnToDressGameContext context, MarkReadyCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "MarkReady (VotingRoundResults): unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            player.IsReady = true;
            context.Logger.LogInformation(
                "Player [{id}] acknowledged round results.", cmd.PlayerId);

            if (!context.AllPlayersReady()) return null;

            // Check whether more voting rounds remain.
            // ResolveRoundCount uses the configured value when positive, or auto-calculates
            // from the entrant count when VotingRounds = 0 (auto mode).
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
