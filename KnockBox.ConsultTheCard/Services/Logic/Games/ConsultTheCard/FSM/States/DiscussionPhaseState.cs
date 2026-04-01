using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States
{
    /// <summary>
    /// Combined discussion and voting phase. Players discuss clues, cast and lock-in votes
    /// to eliminate another player. Any alive player may vote to end the game or vote to
    /// skip the remaining time (once per elimination cycle, rescindable). The host can
    /// force advance. If timers are enabled, auto-advances on timeout. When the phase ends
    /// (skip majority, timeout, host advance, or all votes locked in), tallies votes and
    /// transitions to <see cref="RevealPhaseState"/>.
    /// </summary>
    public sealed class DiscussionPhaseState : ITimedConsultTheCardGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            context.State.SetPhase(ConsultTheCardGamePhase.Discussion);

            // Set up end-game vote tracking for this cycle.
            int aliveCount = context.GetAlivePlayerCount();
            int required = (aliveCount / 2) + 1; // strict majority
            context.State.EndGameVoteStatus = new EndGameVoteStatus([], required);

            // Set up skip-time vote tracking for this cycle.
            context.State.SkipTimeVoteStatus = new EndGameVoteStatus([], required);

            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.DiscussionPhaseTimeoutMs);

            context.Logger.LogInformation("FSM → DiscussionPhaseState");
            return null;
        }

        public Result OnExit(ConsultTheCardGameContext context) => Result.Success;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> HandleCommand(
            ConsultTheCardGameContext context, ConsultTheCardCommand command)
        {
            return command switch
            {
                VoteToEndGameCommand cmd => HandleVoteToEndGame(context, cmd),
                SkipRemainingTimeCommand cmd => HandleSkipRemainingTime(context, cmd),
                AdvanceToVoteCommand cmd => HandleAdvanceToVote(context, cmd),
                CastVoteCommand cmd => HandleCastVote(context, cmd),
                LockInVoteCommand cmd => HandleLockInVote(context, cmd),
                _ => (ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>)null!
            };
        }

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Auto-advance: tally locked-in votes and transition to reveal.
            context.Logger.LogInformation("DiscussionPhase: timed out; tallying votes and advancing to RevealPhaseState.");
            return TallyAndTransition(context);
        }

        public ValueResult<TimeSpan> GetRemainingTime(ConsultTheCardGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private handlers ──────────────────────────────────────────────────

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleVoteToEndGame(ConsultTheCardGameContext context, VoteToEndGameCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null || player.IsEliminated)
                return new ResultError("Only alive players may vote to end the game.");

            // Toggle: rescind if already voted.
            if (player.HasVotedToEndGame)
            {
                player.HasVotedToEndGame = false;
                context.State.EndGameVoteStatus.VotedToEnd.Remove(cmd.PlayerId);

                context.Logger.LogInformation(
                    "DiscussionPhase: [{pid}] rescinded vote to end game ({count}/{required}).",
                    cmd.PlayerId,
                    context.State.EndGameVoteStatus.VotedToEnd.Count,
                    context.State.EndGameVoteStatus.RequiredVotes);

                return null;
            }

            player.HasVotedToEndGame = true;
            context.State.EndGameVoteStatus.VotedToEnd.Add(cmd.PlayerId);

            context.Logger.LogInformation(
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

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleSkipRemainingTime(ConsultTheCardGameContext context, SkipRemainingTimeCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null || player.IsEliminated)
                return new ResultError("Only alive players may vote to skip time.");

            // Toggle: rescind if already voted.
            if (player.HasVotedToSkipTime)
            {
                player.HasVotedToSkipTime = false;
                context.State.SkipTimeVoteStatus.VotedToEnd.Remove(cmd.PlayerId);

                context.Logger.LogInformation(
                    "DiscussionPhase: [{pid}] rescinded vote to skip time ({count}/{required}).",
                    cmd.PlayerId,
                    context.State.SkipTimeVoteStatus.VotedToEnd.Count,
                    context.State.SkipTimeVoteStatus.RequiredVotes);

                return null;
            }

            player.HasVotedToSkipTime = true;
            context.State.SkipTimeVoteStatus.VotedToEnd.Add(cmd.PlayerId);

            context.Logger.LogInformation(
                "DiscussionPhase: [{pid}] voted to skip time ({count}/{required}).",
                cmd.PlayerId,
                context.State.SkipTimeVoteStatus.VotedToEnd.Count,
                context.State.SkipTimeVoteStatus.RequiredVotes);

            // Check if majority reached — tally votes and advance to reveal.
            var status = context.State.SkipTimeVoteStatus;
            if (status.VotedToEnd.Count >= status.RequiredVotes)
            {
                context.Logger.LogInformation("DiscussionPhase: majority voted to skip time; tallying votes.");
                return TallyAndTransition(context);
            }

            return null;
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleAdvanceToVote(ConsultTheCardGameContext context, AdvanceToVoteCommand cmd)
        {
            // Host-only command.
            if (cmd.PlayerId != context.State.Host.Id)
                return new ResultError("Only the host can advance the phase.");

            context.Logger.LogInformation("DiscussionPhase: host advanced; tallying votes and transitioning to RevealPhaseState.");
            return TallyAndTransition(context);
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleCastVote(ConsultTheCardGameContext context, CastVoteCommand cmd)
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

                context.Logger.LogInformation(
                    "DiscussionPhase: [{voter}] removed vote target [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }
            else
            {
                voter.VoteTargetId = cmd.TargetPlayerId;

                context.Logger.LogInformation(
                    "DiscussionPhase: [{voter}] selected vote target [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }

            return null;
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleLockInVote(ConsultTheCardGameContext context, LockInVoteCommand cmd)
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

            context.Logger.LogInformation(
                "DiscussionPhase: [{voter}] locked in vote for [{target}].", cmd.PlayerId, voter.VoteTargetId);

            // Check if all alive players have locked in.
            if (context.GetAlivePlayers().All(p => p.HasVoted))
            {
                context.Logger.LogInformation("DiscussionPhase: all alive players voted; tallying.");
                return TallyAndTransition(context);
            }

            return null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Abstains non-voters, tallies locked-in votes, and transitions to
        /// <see cref="RevealPhaseState"/>.
        /// </summary>
        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            TallyAndTransition(ConsultTheCardGameContext context)
        {
            // Abstain non-voters.
            foreach (var player in context.GetAlivePlayers().Where(p => !p.HasVoted))
            {
                player.HasVoted = true;
                // VoteTargetId remains null — counts as abstain.
                context.Logger.LogInformation(
                    "DiscussionPhase: [{pid}] abstained.", player.PlayerId);
            }

            string? eliminatedId = context.TallyVotes();

            if (eliminatedId is not null)
            {
                var eliminated = context.GetPlayer(eliminatedId)!;
                eliminated.IsEliminated = true;
                context.State.LastElimination = new EliminationResult(
                    eliminated.PlayerId, eliminated.DisplayName, eliminated.Role, WasTie: false);

                context.Logger.LogInformation(
                    "DiscussionPhase: [{pid}] eliminated.", eliminatedId);
            }
            else
            {
                // Tie — no one eliminated.
                context.State.LastElimination = new EliminationResult(
                    string.Empty, string.Empty, default, WasTie: true);

                context.Logger.LogInformation("DiscussionPhase: vote resulted in a tie.");
            }

            return new RevealPhaseState();
        }
    }
}
