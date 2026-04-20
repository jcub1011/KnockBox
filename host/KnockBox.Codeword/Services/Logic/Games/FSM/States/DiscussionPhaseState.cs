using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;

namespace KnockBox.Codeword.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Combined discussion and voting phase. Players discuss clues, cast and lock-in votes
    /// to eliminate another player. Any alive player may vote to end the game or vote to
    /// skip the remaining time (once per elimination cycle, rescindable). The host can
    /// force advance. If timers are enabled, auto-advances on timeout. When the phase ends
    /// (skip majority, timeout, host advance, or all votes locked in), tallies votes and
    /// transitions to <see cref="RevealPhaseState"/>.
    /// </summary>
    public sealed class DiscussionPhaseState : ITimedCodewordGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> OnEnter(CodewordGameContext context)
        {
            context.State.SetPhase(CodewordGamePhase.Discussion);

            // Set up end-game vote tracking for this cycle.
            int aliveCount = context.GetAlivePlayerCount();
            int required = (aliveCount / 2) + 1; // strict majority
            context.State.EndGameVoteStatus = new EndGameVoteStatus([], required);

            // Set up skip-time vote tracking for this cycle.
            context.State.SkipTimeVoteStatus = new EndGameVoteStatus([], required);

            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.DiscussionPhaseTimeoutMs);

            context.Logger.LogDebug("FSM → DiscussionPhaseState");
            return null;
        }

        public Result OnExit(CodewordGameContext context) => Result.Success;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> HandleCommand(
            CodewordGameContext context, CodewordCommand command)
        {
            return command switch
            {
                VoteToEndGameCommand cmd => HandleVoteToEndGame(context, cmd),
                SkipRemainingTimeCommand cmd => HandleSkipRemainingTime(context, cmd),
                AdvanceToVoteCommand cmd => HandleAdvanceToVote(context, cmd),
                CastVoteCommand cmd => HandleCastVote(context, cmd),
                LockInVoteCommand cmd => HandleLockInVote(context, cmd),
                _ => (ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>)null!
            };
        }

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> Tick(
            CodewordGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Auto-advance: tally locked-in votes and transition to reveal.
            context.Logger.LogDebug("DiscussionPhase: timed out; tallying votes and advancing to RevealPhaseState.");
            return TallyAndTransition(context);
        }

        public ValueResult<TimeSpan> GetRemainingTime(CodewordGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private handlers ──────────────────────────────────────────────────

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            HandleVoteToEndGame(CodewordGameContext context, VoteToEndGameCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null || player.IsEliminated)
                return new ResultError("Only alive players may vote to end the game.");

            // Toggle: rescind if already voted.
            if (player.HasVotedToEndGame)
            {
                player.HasVotedToEndGame = false;
                context.State.EndGameVoteStatus.VotedToEnd.Remove(cmd.PlayerId);

                context.Logger.LogDebug(
                    "DiscussionPhase: [{pid}] rescinded vote to end game ({count}/{required}).",
                    cmd.PlayerId,
                    context.State.EndGameVoteStatus.VotedToEnd.Count,
                    context.State.EndGameVoteStatus.RequiredVotes);

                return null;
            }

            player.HasVotedToEndGame = true;
            context.State.EndGameVoteStatus.VotedToEnd.Add(cmd.PlayerId);

            context.Logger.LogDebug(
                "DiscussionPhase: [{pid}] voted to end game ({count}/{required}).",
                cmd.PlayerId,
                context.State.EndGameVoteStatus.VotedToEnd.Count,
                context.State.EndGameVoteStatus.RequiredVotes);

            // Check if majority reached.
            var status = context.State.EndGameVoteStatus;
            if (status.VotedToEnd.Count >= status.RequiredVotes)
            {
                var winResult = context.CheckWinConditions();
                context.State.WinResult = winResult;
                return new GameOverState();
            }

            return null;
        }

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            HandleSkipRemainingTime(CodewordGameContext context, SkipRemainingTimeCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null || player.IsEliminated)
                return new ResultError("Only alive players may vote to skip time.");

            // Toggle: rescind if already voted.
            if (player.HasVotedToSkipTime)
            {
                player.HasVotedToSkipTime = false;
                context.State.SkipTimeVoteStatus.VotedToEnd.Remove(cmd.PlayerId);

                context.Logger.LogDebug(
                    "DiscussionPhase: [{pid}] rescinded vote to skip time ({count}/{required}).",
                    cmd.PlayerId,
                    context.State.SkipTimeVoteStatus.VotedToEnd.Count,
                    context.State.SkipTimeVoteStatus.RequiredVotes);

                return null;
            }

            player.HasVotedToSkipTime = true;
            context.State.SkipTimeVoteStatus.VotedToEnd.Add(cmd.PlayerId);

            context.Logger.LogDebug(
                "DiscussionPhase: [{pid}] voted to skip time ({count}/{required}).",
                cmd.PlayerId,
                context.State.SkipTimeVoteStatus.VotedToEnd.Count,
                context.State.SkipTimeVoteStatus.RequiredVotes);

            // Check if majority reached — tally votes and advance to reveal.
            var status = context.State.SkipTimeVoteStatus;
            if (status.VotedToEnd.Count >= status.RequiredVotes)
            {
                context.Logger.LogDebug("DiscussionPhase: majority voted to skip time; tallying votes.");
                return TallyAndTransition(context);
            }

            return null;
        }

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            HandleAdvanceToVote(CodewordGameContext context, AdvanceToVoteCommand cmd)
        {
            // Host-only command.
            if (cmd.PlayerId != context.State.Host.Id)
                return new ResultError("Only the host can advance the phase.");

            context.Logger.LogDebug("DiscussionPhase: host advanced; tallying votes and transitioning to RevealPhaseState.");
            return TallyAndTransition(context);
        }

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            HandleCastVote(CodewordGameContext context, CastVoteCommand cmd)
        {
            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null || voter.IsEliminated)
                return new ResultError("Only alive players may vote.");

            if (voter.HasVoted)
                return new ResultError("You have already locked in your vote.");

            // Cannot vote for self.
            if (cmd.TargetPlayerId == cmd.PlayerId)
                return new ResultError("You cannot vote for yourself.");

            // Target must be alive.
            var target = context.GetPlayer(cmd.TargetPlayerId);
            if (target is null || target.IsEliminated)
                return new ResultError("You cannot vote for an eliminated player.");

            // Rescind vote
            if (voter.VoteTargetId == cmd.TargetPlayerId)
            {
                voter.VoteTargetId = null;

                context.Logger.LogDebug(
                    "DiscussionPhase: [{voter}] removed vote target [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }
            else
            {
                voter.VoteTargetId = cmd.TargetPlayerId;

                context.Logger.LogDebug(
                    "DiscussionPhase: [{voter}] selected vote target [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }

            return null;
        }

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            HandleLockInVote(CodewordGameContext context, LockInVoteCommand cmd)
        {
            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null || voter.IsEliminated)
                return new ResultError("Only alive players may lock in a vote.");

            if (voter.HasVoted)
                return new ResultError("You have already locked in your vote.");

            if (voter.VoteTargetId is null)
                return new ResultError("You must select a target before locking in.");

            var target = context.GetPlayer(voter.VoteTargetId);
            if (target is null || target.IsEliminated)
                return new ResultError("Your selected target is no longer valid.");

            voter.HasVoted = true;
            context.State.CurrentRoundVotes.Add(
                new VoteEntry(voter.PlayerId, voter.DisplayName, target.PlayerId, target.DisplayName));

            context.Logger.LogDebug(
                "DiscussionPhase: [{voter}] locked in vote for [{target}].", cmd.PlayerId, voter.VoteTargetId);

            // Check if all alive players have locked in.
            if (context.GetAlivePlayers().All(p => p.HasVoted))
            {
                context.Logger.LogDebug("DiscussionPhase: all alive players voted; tallying.");
                return TallyAndTransition(context);
            }

            return null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Abstains non-voters, tallies locked-in votes, and transitions to
        /// <see cref="RevealPhaseState"/>.
        /// </summary>
        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            TallyAndTransition(CodewordGameContext context)
        {
            // Abstain non-voters.
            foreach (var player in context.GetAlivePlayers().Where(p => !p.HasVoted))
            {
                player.HasVoted = true;
                player.VoteTargetId = null; // discard unconfirmed vote
                context.Logger.LogDebug(
                    "DiscussionPhase: [{pid}] abstained.", player.PlayerId);
            }

            string? eliminatedId = context.TallyVotes();

            if (eliminatedId is not null)
            {
                var eliminated = context.GetPlayer(eliminatedId)!;
                eliminated.IsEliminated = true;
                context.State.LastElimination = new EliminationResult(
                    eliminated.PlayerId, eliminated.DisplayName, eliminated.Role, WasTie: false);

                context.Logger.LogDebug(
                    "DiscussionPhase: [{pid}] eliminated.", eliminatedId);
            }
            else
            {
                // Tie — no one eliminated.
                context.State.LastElimination = new EliminationResult(
                    string.Empty, string.Empty, default, WasTie: true);

                context.Logger.LogDebug("DiscussionPhase: vote resulted in a tie.");
            }

            return new RevealPhaseState();
        }
    }
}
