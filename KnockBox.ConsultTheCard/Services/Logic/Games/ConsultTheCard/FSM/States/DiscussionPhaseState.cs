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
                _ => (ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>)null!
            };
        }

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Auto-advance to vote phase.
            context.Logger.LogInformation("DiscussionPhase: timed out; auto-advancing to VotePhaseState.");
            return new VotePhaseState();
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
                return new VotePhaseState();
            }

            return null;
        }

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            HandleAdvanceToVote(ConsultTheCardGameContext context, AdvanceToVoteCommand cmd)
        {
            // Host-only command.
            if (cmd.PlayerId != context.State.Host.Id)
                return new ResultError("Only the host can advance the phase.");

            context.Logger.LogInformation("DiscussionPhase: host advanced to VotePhaseState.");
            return new VotePhaseState();
        }
    }
}
