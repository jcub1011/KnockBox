using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Codeword.Services.State.Games;

namespace KnockBox.Codeword.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Voting phase. Each alive player votes to eliminate another player.
    /// Validates no self-voting and target must be alive. When all alive players
    /// have voted, tallies votes and transitions to <see cref="RevealPhaseState"/>.
    /// </summary>
    public sealed class VotePhaseState : ITimedCodewordGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> OnEnter(CodewordGameContext context)
        {
            context.State.SetPhase(CodewordGamePhase.Voting);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.VotePhaseTimeoutMs);

            context.Logger.LogDebug("FSM → VotePhaseState");
            return null;
        }

        public Result OnExit(CodewordGameContext context) => Result.Success;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> HandleCommand(
            CodewordGameContext context, CodewordCommand command)
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

                context.Logger.LogDebug(
                    "VotePhase: [{voter}] rescinded vote for [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }
            else
            {
                voter.HasVoted = true;
                voter.VoteTargetId = cmd.TargetPlayerId;
                context.State.CurrentRoundVotes.Add(
                    new VoteEntry(voter.PlayerId, voter.DisplayName, target.PlayerId, target.DisplayName));

                context.Logger.LogDebug(
                    "VotePhase: [{voter}] voted for [{target}].", cmd.PlayerId, cmd.TargetPlayerId);
            }

            // Check if all alive players have voted.
            if (context.GetAlivePlayers().All(p => p.HasVoted))
                return TallyAndTransition(context);

            return null;
        }

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> Tick(
            CodewordGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Abstain non-voters on timeout.
            foreach (var player in context.GetAlivePlayers().Where(p => !p.HasVoted))
            {
                player.HasVoted = true;
                player.VoteTargetId = null; // discard unconfirmed vote
                context.Logger.LogDebug(
                    "VotePhase: [{pid}] timed out; abstaining.", player.PlayerId);
            }

            return TallyAndTransition(context);
        }

        public ValueResult<TimeSpan> GetRemainingTime(CodewordGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private helpers ───────────────────────────────────────────────────

        private static ValueResult<IGameState<CodewordGameContext, CodewordCommand>?>
            TallyAndTransition(CodewordGameContext context)
        {
            string? eliminatedId = context.TallyVotes();

            if (eliminatedId is not null)
            {
                var eliminated = context.GetPlayer(eliminatedId)!;
                eliminated.IsEliminated = true;
                context.State.LastElimination = new EliminationResult(
                    eliminated.PlayerId, eliminated.DisplayName, eliminated.Role, WasTie: false);

                context.Logger.LogDebug(
                    "VotePhase: [{pid}] eliminated.", eliminatedId);
            }
            else
            {
                // Tie — no one eliminated.
                context.State.LastElimination = new EliminationResult(
                    string.Empty, string.Empty, default, WasTie: true);

                context.Logger.LogDebug("VotePhase: vote resulted in a tie.");
            }

            return new RevealPhaseState();
        }
    }
}
