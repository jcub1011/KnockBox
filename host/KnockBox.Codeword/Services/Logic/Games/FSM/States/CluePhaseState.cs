using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;

namespace KnockBox.Codeword.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Clue submission phase. Each alive player submits a one-word clue in turn order.
    /// Validates clues (no spaces, not the player's secret word, not previously used).
    /// Skips eliminated players. Transitions to <see cref="DiscussionPhaseState"/> when
    /// all alive players have submitted.
    /// </summary>
    public sealed class CluePhaseState : ITimedCodewordGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> OnEnter(CodewordGameContext context)
        {
            context.ResetEliminationCycleState();
            context.State.SetPhase(CodewordGamePhase.CluePhase);

            // Advance past eliminated players to the next alive player.
            if (!AdvanceToNextAlivePlayer(context))
            {
                // No alive players — should not happen, but transition to GameOver as a safety net.
                context.Logger.LogWarning("CluePhaseState.OnEnter: no alive players found.");
                return new GameOverState();
            }

            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.CluePhaseTimeoutMs);

            context.Logger.LogDebug(
                "FSM → CluePhaseState (current player index: {idx}, player: {pid})",
                context.State.TurnManager.CurrentPlayerIndex,
                context.State.TurnManager.CurrentPlayer);

            return null;
        }

        public Result OnExit(CodewordGameContext context) => Result.Success;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> HandleCommand(
            CodewordGameContext context, CodewordCommand command)
        {
            if (command is not SubmitClueCommand cmd)
                return null;

            string? currentPlayerId = context.State.TurnManager.CurrentPlayer;

            // Only the current player may submit.
            if (cmd.PlayerId != currentPlayerId)
                return new ResultError("It is not your turn to submit a clue.");

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
                return new ResultError("Player not found.");

            if (player.HasSubmittedClue)
                return new ResultError("You have already submitted a clue.");

            // Validate: character limit and format.
            string clue = cmd.Clue.Trim();
            if (string.IsNullOrWhiteSpace(clue))
                return new ResultError("Clue cannot be empty.");
            if (clue.Length > 50)
                return new ResultError("Clue must be 50 characters or less.");

            // Validate: not the player's secret word.
            if (player.SecretWord is not null &&
                string.Equals(clue, player.SecretWord, StringComparison.OrdinalIgnoreCase))
                return new ResultError("You cannot use your own secret word as a clue.");

            // Validate: not previously used by any player in the current game.
            if (context.State.UsedClues.ContainsKey(clue))
                return new ResultError("This clue has already been used in the current game.");

            // Store the clue.
            player.HasSubmittedClue = true;
            player.CurrentClue = clue;
            player.ClueHistory.Add(clue);
            context.State.UsedClues.TryAdd(clue, player.DisplayName);
            context.State.CurrentRoundClues.Add(
                new ClueEntry(player.PlayerId, player.DisplayName, clue));

            context.Logger.LogDebug(
                "CluePhase: [{pid}] submitted clue [{clue}].", cmd.PlayerId, clue);

            // Check if all alive players have submitted.
            if (context.GetAlivePlayers().All(p => p.HasSubmittedClue))
                return new DiscussionPhaseState();

            // Advance to next alive player and reset timer.
            context.State.TurnManager.NextTurn();
            AdvanceToNextAlivePlayer(context);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.CluePhaseTimeoutMs);

            return null;
        }

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> Tick(
            CodewordGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            if (!context.State.Config.EnableTimers)
                return null;

            // Auto-submit for the timed-out player. Use pending clue text if valid, otherwise "...".
            string? currentPlayerId = context.State.TurnManager.CurrentPlayer;
            var player = currentPlayerId is not null ? context.GetPlayer(currentPlayerId) : null;
            if (player is not null && !player.HasSubmittedClue)
            {
                string clue = ResolvePendingClue(context, player);
                player.HasSubmittedClue = true;
                player.CurrentClue = clue;
                player.ClueHistory.Add(clue);
                context.State.UsedClues.TryAdd(clue, player.DisplayName);
                context.State.CurrentRoundClues.Add(
                    new ClueEntry(player.PlayerId, player.DisplayName, clue));

                context.Logger.LogDebug(
                    "CluePhase: [{pid}] timed out; auto-submitted '{clue}'.", currentPlayerId, clue);
            }

            // Check if all alive players have submitted.
            if (context.GetAlivePlayers().All(p => p.HasSubmittedClue))
                return new DiscussionPhaseState();

            // Advance to next alive player and reset timer.
            context.State.TurnManager.NextTurn();
            AdvanceToNextAlivePlayer(context);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.CluePhaseTimeoutMs);

            return null;
        }

        public ValueResult<TimeSpan> GetRemainingTime(CodewordGameContext context, DateTimeOffset now)
            => _expiresAt - now;

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the player's pending clue text if valid, otherwise "...".
        /// Validates: non-empty, ≤50 chars, not the secret word, not previously used.
        /// </summary>
        private static string ResolvePendingClue(CodewordGameContext context, CodewordPlayerState player)
        {
            string? pending = player.PendingClue?.Trim();
            if (string.IsNullOrWhiteSpace(pending))
                return "...";
            if (pending.Length > 50)
                return "...";
            if (player.SecretWord is not null &&
                string.Equals(pending, player.SecretWord, StringComparison.OrdinalIgnoreCase))
                return "...";
            if (context.State.UsedClues.ContainsKey(pending))
                return "...";
            return pending;
        }

        /// <summary>
        /// Advances <see cref="CodewordGameState.TurnManager.CurrentPlayerIndex"/> past eliminated players
        /// to the next alive player. Returns <see langword="false"/> if no alive player is found
        /// (full wrap without hitting an alive player).
        /// </summary>
        private static bool AdvanceToNextAlivePlayer(CodewordGameContext context)
        {
            var turnOrder = context.State.TurnManager.TurnOrder;
            int startIndex = context.State.TurnManager.CurrentPlayerIndex;

            for (int i = 0; i < turnOrder.Count; i++)
            {
                string playerId = turnOrder[context.State.TurnManager.CurrentPlayerIndex];
                var player = context.GetPlayer(playerId);
                if (player is not null && !player.IsEliminated && !player.HasSubmittedClue)
                    return true;

                context.State.TurnManager.NextTurn();
            }

            // Wrapped fully — no alive un-submitted player found.
            context.State.TurnManager.SetCurrentPlayerIndex(startIndex);
            return context.GetAlivePlayerCount() > 0;
        }
    }
}
