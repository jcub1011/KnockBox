using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.ConsultTheCard;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States
{
    /// <summary>
    /// Discussion phase. Players discuss clues. Any alive player may vote to end the game
    /// (once per elimination cycle). The host can advance to the voting phase.
    /// If timers are enabled, auto-advances on timeout.
    /// </summary>
    public sealed class DiscussionPhaseState : ITimedConsultTheCardGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            context.State.GamePhase = ConsultTheCardGamePhase.Discussion;

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
                CastVoteCommand cmd => HandleCastVote(context, cmd),
                LockInVoteCommand cmd => HandleLockInVote(context, cmd),
                AdvanceToVoteCommand cmd => HandleAdvanceToVote(context, cmd),
                _ => (ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>)null!
            };
        }

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Auto-advance to reveal.
            context.Logger.LogInformation("DiscussionPhase: timed out; auto-advancing to RevealPhaseState.");
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

            if (player.HasVotedToEndGame)
                return new ResultError("You have already voted to end the game this cycle.");

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

            if (player.HasVotedToSkipTime)
                return new ResultError("You have already voted to skip the remaining discussion time.");

            player.HasVotedToSkipTime = true;
            context.State.SkipTimeVoteStatus.VotedToEnd.Add(cmd.PlayerId);

            context.Logger.LogInformation(
                "DiscussionPhase: [{pid}] voted to skip time ({count}/{required}).",
                cmd.PlayerId,
                context.State.SkipTimeVoteStatus.VotedToEnd.Count,
                context.State.SkipTimeVoteStatus.RequiredVotes);

            // Check if majority reached.
            var status = context.State.SkipTimeVoteStatus;
            if (status.VotedToEnd.Count >= status.RequiredVotes)
            {
                context.Logger.LogInformation("DiscussionPhase: majority voted to skip time.");
                return TallyAndTransition(context);
            }

            return null;
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

            voter.VoteTargetId = cmd.TargetPlayerId;

            context.Logger.LogInformation(
                "DiscussionPhase: [{voter}] selected [{target}] for voting.", cmd.PlayerId, cmd.TargetPlayerId);

            return null;
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleLockInVote(ConsultTheCardGameContext context, LockInVoteCommand cmd)
        {
            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null || voter.IsEliminated)
                return new ResultError("Only alive players may vote.");

            if (voter.HasVoted)
                return new ResultError("You have already locked in your vote.");

            if (string.IsNullOrEmpty(voter.VoteTargetId))
                return new ResultError("You must select a player to vote for before locking in.");

            var target = context.GetPlayer(voter.VoteTargetId);
            if (target is null || target.IsEliminated)
                return new ResultError("Your vote target is no longer valid.");

            voter.HasVoted = true;
            context.State.CurrentRoundVotes.Add(
                new VoteEntry(voter.PlayerId, voter.DisplayName, target.PlayerId, target.DisplayName));

            context.Logger.LogInformation(
                "DiscussionPhase: [{voter}] locked in vote for [{target}].", cmd.PlayerId, voter.VoteTargetId);

            // Check if all alive players have voted and locked in.
            if (context.GetAlivePlayers().All(p => p.HasVoted))
            {
                context.Logger.LogInformation("DiscussionPhase: all players voted; auto-advancing.");
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

            context.Logger.LogInformation("DiscussionPhase: host advanced to RevealPhaseState.");
            return TallyAndTransition(context);
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            TallyAndTransition(ConsultTheCardGameContext context)
        {
            // Abstain non-voters.
            foreach (var player in context.GetAlivePlayers().Where(p => !p.HasVoted))
            {
                player.HasVoted = true;
                context.Logger.LogInformation(
                    "DiscussionPhase: [{pid}] did not lock in vote; abstaining.", player.PlayerId);
            }

            string? eliminatedId = context.TallyVotes();

            // Apply per-cycle scoring.
            context.ApplyCycleScoring(eliminatedId);

            if (eliminatedId is not null)
            {
                var eliminated = context.GetPlayer(eliminatedId)!;
                eliminated.IsEliminated = true;
                context.State.LastElimination = new EliminationResult(
                    eliminated.PlayerId, eliminated.DisplayName, eliminated.Role, WasTie: false);

                context.Logger.LogInformation(
                    "DiscussionPhase: [{pid}] eliminated.", eliminatedId);

                // If Informant is eliminated, they get a chance to guess.
                // This MUST go to RevealPhaseState to handle the guess logic.
                if (eliminated.Role == Role.Informant)
                {
                    return new RevealPhaseState();
                }

                // Non-Informant eliminated — check win conditions.
                var winResult = context.CheckWinConditions();
                if (winResult.GameOver)
                {
                    context.State.WinResult = winResult;
                    return new GameOverState();
                }
            }
            else
            {
                // Tie — no one eliminated.
                context.State.LastElimination = new EliminationResult(
                    string.Empty, string.Empty, default, WasTie: true);

                context.Logger.LogInformation("DiscussionPhase: vote resulted in a tie.");
            }

            // Reset for next cycle and go to CluePhase.
            context.ResetEliminationCycleState();
            return new CluePhaseState();
        }
    }
}
