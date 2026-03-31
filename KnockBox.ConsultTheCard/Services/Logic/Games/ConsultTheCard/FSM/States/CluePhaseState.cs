using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.ConsultTheCard;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States
{
    /// <summary>
    /// Clue submission phase. Each alive player submits a one-word clue in turn order.
    /// Validates clues (no spaces, not the player's secret word, not previously used).
    /// Skips eliminated players. Transitions to <see cref="DiscussionPhaseState"/> when
    /// all alive players have submitted.
    /// </summary>
    public sealed class CluePhaseState : ITimedConsultTheCardGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            context.State.GamePhase = ConsultTheCardGamePhase.CluePhase;

            // Advance past eliminated players to the next alive player.
            if (!AdvanceToNextAlivePlayer(context))
            {
                // No alive players — should not happen, but transition to GameOver as a safety net.
                context.Logger.LogWarning("CluePhaseState.OnEnter: no alive players found.");
                return new GameOverState();
            }

            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.CluePhaseTimeoutMs);

            context.Logger.LogInformation(
                "FSM → CluePhaseState (current player index: {idx}, player: {pid})",
                context.State.CurrentCluePlayerIndex,
                context.State.TurnOrder[context.State.CurrentCluePlayerIndex]);

            return null;
        }

        public Result OnExit(ConsultTheCardGameContext context) => Result.Success;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> HandleCommand(
            ConsultTheCardGameContext context, ConsultTheCardCommand command)
        {
            if (command is not SubmitClueCommand cmd)
                return null;

            var turnOrder = context.State.TurnOrder;
            string currentPlayerId = turnOrder[context.State.CurrentCluePlayerIndex];

            // Only the current player may submit.
            if (cmd.PlayerId != currentPlayerId)
                return new ResultError("It is not your turn to submit a clue.");

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
                return new ResultError("Player not found.");

            if (player.HasSubmittedClue)
                return new ResultError("You have already submitted a clue.");

            // Validate: single word (no spaces).
            string clue = cmd.Clue.Trim();
            if (string.IsNullOrWhiteSpace(clue) || clue.Contains(' '))
                return new ResultError("Clue must be a single word with no spaces.");

            // Validate: not the player's secret word.
            if (player.SecretWord is not null &&
                string.Equals(clue, player.SecretWord, StringComparison.OrdinalIgnoreCase))
                return new ResultError("You cannot use your own secret word as a clue.");

            // Validate: not previously used by any player in the current game.
            if (context.State.UsedClues.Contains(clue))
                return new ResultError("This clue has already been used in the current game.");

            // Store the clue.
            player.HasSubmittedClue = true;
            player.CurrentClue = clue;
            context.State.UsedClues.Add(clue);
            context.State.CurrentRoundClues.Add(
                new ClueEntry(player.PlayerId, player.DisplayName, clue));

            context.Logger.LogInformation(
                "CluePhase: [{pid}] submitted clue [{clue}].", cmd.PlayerId, clue);

            // Check if all alive players have submitted.
            if (context.GetAlivePlayers().All(p => p.HasSubmittedClue))
                return new DiscussionPhaseState();

            // Advance to next alive player and reset timer.
            AdvanceCluePlayerIndex(context);
            AdvanceToNextAlivePlayer(context);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.CluePhaseTimeoutMs);

            return null;
        }

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Auto-submit "..." for the timed-out player.
            string currentPlayerId = context.State.TurnOrder[context.State.CurrentCluePlayerIndex];
            var player = context.GetPlayer(currentPlayerId);
            if (player is not null && !player.HasSubmittedClue)
            {
                string defaultClue = "...";
                player.HasSubmittedClue = true;
                player.CurrentClue = defaultClue;
                context.State.UsedClues.Add(defaultClue);
                context.State.CurrentRoundClues.Add(
                    new ClueEntry(player.PlayerId, player.DisplayName, defaultClue));

                context.Logger.LogInformation(
                    "CluePhase: [{pid}] timed out; auto-submitted '...'.", currentPlayerId);
            }

            // Check if all alive players have submitted.
            if (context.GetAlivePlayers().All(p => p.HasSubmittedClue))
                return new DiscussionPhaseState();

            // Advance to next alive player and reset timer.
            AdvanceCluePlayerIndex(context);
            AdvanceToNextAlivePlayer(context);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.CluePhaseTimeoutMs);

            return null;
        }

        public ValueResult<TimeSpan> GetRemainingTime(ConsultTheCardGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Advances <see cref="ConsultTheCardGameState.CurrentCluePlayerIndex"/> by one (wrapping).
        /// </summary>
        private static void AdvanceCluePlayerIndex(ConsultTheCardGameContext context)
        {
            var turnOrder = context.State.TurnOrder;
            context.State.CurrentCluePlayerIndex =
                (context.State.CurrentCluePlayerIndex + 1) % turnOrder.Count;
        }

        /// <summary>
        /// Advances <see cref="ConsultTheCardGameState.CurrentCluePlayerIndex"/> past eliminated players
        /// to the next alive player. Returns <see langword="false"/> if no alive player is found
        /// (full wrap without hitting an alive player).
        /// </summary>
        private static bool AdvanceToNextAlivePlayer(ConsultTheCardGameContext context)
        {
            var turnOrder = context.State.TurnOrder;
            int startIndex = context.State.CurrentCluePlayerIndex;

            for (int i = 0; i < turnOrder.Count; i++)
            {
                string playerId = turnOrder[context.State.CurrentCluePlayerIndex];
                var player = context.GetPlayer(playerId);
                if (player is not null && !player.IsEliminated && !player.HasSubmittedClue)
                    return true;

                context.State.CurrentCluePlayerIndex =
                    (context.State.CurrentCluePlayerIndex + 1) % turnOrder.Count;
            }

            // Wrapped fully — no alive un-submitted player found.
            context.State.CurrentCluePlayerIndex = startIndex;
            return context.GetAlivePlayerCount() > 0;
        }
    }
}
