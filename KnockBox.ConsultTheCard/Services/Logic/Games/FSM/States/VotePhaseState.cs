using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.ConsultTheCard.Services.State.Games;

namespace KnockBox.ConsultTheCard.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Voting phase. Each alive player votes to eliminate another player.
    /// Validates no self-voting and target must be alive. When all alive players
    /// have voted, tallies votes and transitions to <see cref="RevealPhaseState"/>.
    /// </summary>
    public sealed class VotePhaseState : ITimedConsultTheCardGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            context.State.SetPhase(ConsultTheCardGamePhase.Voting);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.VotePhaseTimeoutMs);

            context.Logger.LogInformation("FSM → VotePhaseState");
            return null;
        }

        public Result OnExit(ConsultTheCardGameContext context) => Result.Success;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> HandleCommand(
            ConsultTheCardGameContext context, ConsultTheCardCommand command)
        {
            if (command is not CastVoteCommand cmd)
                return null;

            var voter = context.GetPlayer(cmd.PlayerId);
            if (voter is null || voter.IsEliminated)
                return new ResultError("Only alive players may vote.");

            if (voter.HasVoted)
                return new ResultError("You have already voted.");

            // Cannot vote for self.
            if (cmd.TargetPlayerId == cmd.PlayerId)
                return new ResultError("You cannot vote for yourself.");

            // Target must be alive.
            var target = context.GetPlayer(cmd.TargetPlayerId);
            if (target is null || target.IsEliminated)
                return new ResultError("You cannot vote for an eliminated player.");

            // Remove existing vote
            if (voter.VoteTargetId == cmd.TargetPlayerId)
            {
                voter.HasVoted = false;
                voter.VoteTargetId = null;
                context.State.CurrentRoundVotes.RemoveAll((entry) =>
                {
                    return entry.VoterId == cmd.PlayerId && entry.TargetId == cmd.TargetPlayerId;
                });

                context.Logger.LogInformation(
                    "VotePhase: [{voter}] rescinded vote for [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }
            else
            {
                voter.HasVoted = true;
                voter.VoteTargetId = cmd.TargetPlayerId;
                context.State.CurrentRoundVotes.Add(
                    new VoteEntry(voter.PlayerId, voter.DisplayName, target.PlayerId, target.DisplayName));

                context.Logger.LogInformation(
                    "VotePhase: [{voter}] voted for [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }

            // Check if all alive players have voted.
            if (context.GetAlivePlayers().All(p => p.HasVoted))
                return TallyAndTransition(context);

            return null;
        }

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Abstain non-voters on timeout.
            foreach (var player in context.GetAlivePlayers().Where(p => !p.HasVoted))
            {
                player.HasVoted = true;
                player.VoteTargetId = null; // discard unconfirmed vote
                context.Logger.LogInformation(
                    "VotePhase: [{pid}] timed out; abstaining.", player.PlayerId);
            }

            return TallyAndTransition(context);
        }

        public ValueResult<TimeSpan> GetRemainingTime(ConsultTheCardGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private helpers ───────────────────────────────────────────────────

        private static ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?>
            TallyAndTransition(ConsultTheCardGameContext context)
        {
            string? eliminatedId = context.TallyVotes();

            if (eliminatedId is not null)
            {
                var eliminated = context.GetPlayer(eliminatedId)!;
                eliminated.IsEliminated = true;
                context.State.LastElimination = new EliminationResult(
                    eliminated.PlayerId, eliminated.DisplayName, eliminated.Role, WasTie: false);

                context.Logger.LogInformation(
                    "VotePhase: [{pid}] eliminated.", eliminatedId);
            }
            else
            {
                // Tie — no one eliminated.
                context.State.LastElimination = new EliminationResult(
                    string.Empty, string.Empty, default, WasTie: true);

                context.Logger.LogInformation("VotePhase: vote resulted in a tie.");
            }

            return new RevealPhaseState();
        }
    }
}
