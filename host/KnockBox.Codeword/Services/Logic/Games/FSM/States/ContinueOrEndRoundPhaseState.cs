using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Codeword.Services.State.Games;

namespace KnockBox.Codeword.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Post-elimination decision phase. After a non-Informant elimination where
    /// the game has not auto-ended, alive players vote whether to continue with
    /// another round of clue-giving or end the game now. Majority "end" votes
    /// transition to <see cref="GameOverState"/>; otherwise — including timeout
    /// with no majority — the FSM transitions back to <see cref="CluePhaseState"/>.
    /// </summary>
    public sealed class ContinueOrEndRoundPhaseState : ITimedCodewordGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> OnEnter(CodewordGameContext context)
        {
            context.State.SetPhase(CodewordGamePhase.ContinueOrEndRound);

            // Fresh per-phase tally. Required = strict majority of alive players.
            int aliveCount = context.GetAlivePlayerCount();
            int required = (aliveCount / 2) + 1;
            context.State.EndGameVoteStatus = new EndGameVoteStatus([], required);

            // Reset the per-phase vote tracker on every alive player. Eliminated
            // players keep whatever stale value they had — we only ever read this
            // for alive players in the tally.
            foreach (var ps in context.GetAlivePlayers())
            {
                ps.ContinueOrEndVote = null;
            }

            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
                context.State.Settings.ContinueOrEndRoundPhaseTimeoutMs);

            context.Logger.LogDebug(
                "FSM → ContinueOrEndRoundPhaseState (alive={alive}, required={req})",
                aliveCount, required);

            return null;
        }

        public Result OnExit(CodewordGameContext context) => Result.Success;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> HandleCommand(
            CodewordGameContext context, CodewordCommand command)
        {
            if (command is not ContinueOrEndRoundVoteCommand cmd)
                return null;

            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null || voter.IsEliminated)
                return new ResultError("Only alive players may vote.");

            // Toggle: a second click on the same option rescinds the vote.
            if (voter.ContinueOrEndVote == cmd.VoteToEnd)
            {
                voter.ContinueOrEndVote = null;
                context.State.EndGameVoteStatus.VotedToEnd.Remove(cmd.PlayerId);

                context.Logger.LogDebug(
                    "ContinueOrEndRound: [{pid}] rescinded vote.", cmd.PlayerId);

                return null;
            }

            voter.ContinueOrEndVote = cmd.VoteToEnd;
            if (cmd.VoteToEnd)
                context.State.EndGameVoteStatus.VotedToEnd.Add(cmd.PlayerId);
            else
                context.State.EndGameVoteStatus.VotedToEnd.Remove(cmd.PlayerId);

            context.Logger.LogDebug(
                "ContinueOrEndRound: [{pid}] voted [{choice}] ({end}/{required}).",
                cmd.PlayerId,
                cmd.VoteToEnd ? "END" : "CONTINUE",
                context.State.EndGameVoteStatus.VotedToEnd.Count,
                context.State.EndGameVoteStatus.RequiredVotes);

            // Early exit once a majority decides "end".
            if (context.State.EndGameVoteStatus.VotedToEnd.Count
                >= context.State.EndGameVoteStatus.RequiredVotes)
            {
                return TransitionToGameOver(context);
            }

            // If every alive player has cast a vote, transition based on tally.
            if (context.GetAlivePlayers().All(p => p.ContinueOrEndVote.HasValue))
                return TallyAndTransition(context);

            return null;
        }

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> Tick(
            CodewordGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Settings.EnableTimers)
                return null;

            // Non-voters default to "continue" on timeout — keep playing unless
            // someone actively asks to stop.
            foreach (var player in context.GetAlivePlayers().Where(p => !p.ContinueOrEndVote.HasValue))
            {
                player.ContinueOrEndVote = false;
                context.Logger.LogDebug(
                    "ContinueOrEndRound: [{pid}] timed out; defaulting to continue.",
                    player.PlayerId);
            }

            return TallyAndTransition(context);
        }

        public ValueResult<TimeSpan> GetRemainingTime(CodewordGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private helpers ───────────────────────────────────────────────────

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            TallyAndTransition(CodewordGameContext context)
        {
            var status = context.State.EndGameVoteStatus;
            if (status.VotedToEnd.Count >= status.RequiredVotes)
                return TransitionToGameOver(context);

            context.Logger.LogDebug(
                "ContinueOrEndRound: continue won ({end}/{required}); resuming clue rounds.",
                status.VotedToEnd.Count, status.RequiredVotes);
            return new CluePhaseState();
        }

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            TransitionToGameOver(CodewordGameContext context)
        {
            // CheckWinConditions sees EndGameVoteStatus.VotedToEnd >= RequiredVotes
            // and returns GameOver=true with the appropriate winning team using
            // the same priority as elsewhere (Informant > Insider > Agent).
            context.State.WinResult = context.CheckWinConditions();
            context.Logger.LogDebug(
                "ContinueOrEndRound: end won ({end}/{required}); ending game.",
                context.State.EndGameVoteStatus.VotedToEnd.Count,
                context.State.EndGameVoteStatus.RequiredVotes);
            return new GameOverState();
        }
    }
}
