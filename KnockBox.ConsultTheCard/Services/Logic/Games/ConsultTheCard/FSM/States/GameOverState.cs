using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States
{
    /// <summary>
    /// Terminal state for a single game. Applies end-of-game scoring, handles
    /// multi-game progression (<see cref="StartNextGameCommand"/>), and
    /// returning to the lobby (<see cref="ReturnToLobbyCommand"/>).
    /// </summary>
    public sealed class GameOverState : IConsultTheCardGameState
    {
        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            // Evaluate win conditions if not already set (e.g. from reveal phase).
            context.State.WinResult ??= context.CheckWinConditions();

            context.ApplyEndOfGameScoring(context.State.WinResult);
            context.State.SetPhase(ConsultTheCardGamePhase.GameOver);

            context.Logger.LogInformation(
                "FSM → GameOverState (game {num}, winner: {team})",
                context.State.CurrentGameNumber,
                context.State.WinResult.WinningTeam);

            return null;
        }

        public Result OnExit(ConsultTheCardGameContext context) => Result.Success;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> HandleCommand(
            ConsultTheCardGameContext context, ConsultTheCardCommand command)
        {
            return command switch
            {
                StartNextGameCommand cmd => HandleStartNextGame(context, cmd),
                ReturnToLobbyCommand cmd => HandleReturnToLobby(context, cmd),
                _ => (ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>)null!
            };
        }

        // ── Private handlers ──────────────────────────────────────────────────

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleStartNextGame(ConsultTheCardGameContext context, StartNextGameCommand cmd)
        {
            // Host-only.
            if (cmd.PlayerId != context.State.Host.Id)
                return new ResultError("Only the host can start the next game.");

            if (context.State.CurrentGameNumber >= context.State.Config.TotalGames)
                return new ResultError("All games have been played.");

            context.State.CurrentGameNumber++;

            // Clear per-game player state.
            foreach (var ps in context.GamePlayers.Values)
            {
                ps.Role = default;
                ps.SecretWord = null;
                ps.IsEliminated = false;
                ps.HasSubmittedClue = false;
                ps.CurrentClue = null;
                ps.ClueHistory.Clear();
                ps.VoteTargetId = null;
                ps.HasVoted = false;
                ps.HasVotedToEndGame = false;
                ps.Score = 0;
            }

            // Reset game-level state.
            context.State.CurrentEliminationCycle = 0;
            context.State.TurnManager.SetCurrentPlayerIndex(0);
            context.State.CurrentWordPair = null;
            context.State.CurrentRoundClues.Clear();
            context.State.CurrentRoundVotes.Clear();
            context.State.UsedClues.Clear();
            context.State.LastElimination = null;
            context.State.LastInformantGuess = null;
            context.State.AwaitingInformantGuess = false;
            context.State.WinResult = null;
            context.State.EndGameVoteStatus = new EndGameVoteStatus([], 0);

            // Re-randomize TurnOrder.
            var turnOrder = context.State.TurnManager.TurnOrder;
            for (int i = turnOrder.Count - 1; i > 0; i--)
            {
                int j = context.Rng.GetRandomInt(0, i + 1);
                (turnOrder[i], turnOrder[j]) = (turnOrder[j], turnOrder[i]);
            }

            // Preserve: UsedWordPairIndices, GameScores (cumulative).

            context.Logger.LogInformation(
                "GameOver: starting game {num}.", context.State.CurrentGameNumber);

            return new SetupState();
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleReturnToLobby(ConsultTheCardGameContext context, ReturnToLobbyCommand cmd)
        {
            // Host-only.
            if (cmd.PlayerId != context.State.Host.Id)
                return new ResultError("Only the host can return to the lobby.");

            context.Logger.LogInformation("GameOver: returning to lobby.");

            // Return null to signal a lobby transition. The engine handles the actual
            // lobby state creation externally.
            return null;
        }
    }
}
