using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    using FsmState = IGameState<AlphaChainGameContext, AlphaChainCommand>;

    /// <summary>
    /// The core turn loop. Players submit words via <see cref="SubmitWordCommand"/>;
    /// the chain (succession) rule, uniqueness, dictionary validation, the Zero-Point
    /// Tax, and length-only scoring are enforced here. The shot clock is a real timer:
    /// <see cref="Tick"/> zeroes a player's turn (or eliminates them in Survival mode)
    /// when it runs out. When the turn order wraps the canonical era/round rule decides
    /// whether the game ends.
    /// </summary>
    public sealed class RoundState : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.Round);
            state.ResetTurnTimer(DateTimeOffset.UtcNow);

            context.Logger.LogDebug(
                "Alpha Chain FSM → RoundState (era {era}, round {round}, active {player})",
                state.CurrentEra, state.CurrentRound, state.TurnManager.CurrentPlayer);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<TimeSpan> GetRemainingTime(AlphaChainGameContext context, DateTimeOffset now)
        {
            var remaining = context.State.PhaseEndTime - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        public ValueResult<FsmState?> HandleCommand(AlphaChainGameContext context, AlphaChainCommand command)
        {
            return command switch
            {
                SubmitWordCommand cmd => HandleSubmitWord(context, cmd),
                AdvanceTurnCommand cmd => HandleAdvanceTurn(context, cmd),
                _ => (ValueResult<FsmState?>)null!
            };
        }

        // ── Word submission ──────────────────────────────────────────────────

        private static ValueResult<FsmState?> HandleSubmitWord(AlphaChainGameContext context, SubmitWordCommand cmd)
        {
            var state = context.State;
            var turnManager = state.TurnManager;

            // 1. Only the active player may submit.
            if (cmd.ActorUserId != turnManager.CurrentPlayer)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedNotYourTurn();
                return null;
            }

            // 2. Normalize: trim + lower-case (IsValidWord is case-insensitive, but the
            //    chain/uniqueness checks compare against the normalized form).
            string word = cmd.WordRaw.Trim().ToLowerInvariant();

            // 3. Empty after trimming.
            if (word.Length == 0)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedEmpty();
                return null;
            }

            // 4. Chain (succession) rule — first letter must match the required start letter.
            if (state.RequiredStartLetter is { } required && word[0] != required)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedChainBroken(required);
                return null;
            }

            // 5. Uniqueness (case-insensitive via the set's comparer).
            if (state.PlayedWords.Contains(word))
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedDuplicate();
                return null;
            }

            // 6. Dictionary membership (full dictionary; non-ASCII/non-letters → false).
            if (!context.WordList.IsValidWord(word))
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedNotInDictionary();
                return null;
            }

            // 7. Zero-Point Tax: the banned letter anywhere in the word zeroes the score.
            bool containsBanned = state.BannedLetter is { } banned && word.Contains(banned);

            // 8. Baseline scoring is length-only (full pipeline lands in M3).
            int score = containsBanned ? 0 : word.Length;

            // 9. Record the play and update the chain. A banned letter *as the last letter*
            //    clears the required start letter (free choice for the next player).
            state.PlayedWords.Add(word);
            state.LastWord = word;
            state.RequiredStartLetter =
                (state.BannedLetter is { } b && b == word[^1]) ? null : word[^1];

            // 10. Credit the player.
            if (state.GamePlayers.TryGetValue(cmd.ActorUserId, out var player))
                player.Score += score;

            // Log the accepted play for the UI feed.
            state.PlayLog.Add(new AlphaChainWordPlay(
                DateTimeOffset.UtcNow,
                cmd.ActorUserId,
                player?.DisplayName ?? cmd.ActorUserId,
                word,
                score,
                containsBanned));

            // 13. Result is set before advancing so it reflects this submission.
            context.LastSubmitResult = containsBanned
                ? new SubmitWordResult.AcceptedZeroPointTax()
                : new SubmitWordResult.Accepted(score);

            // 11 + 12. Advance the turn (round increment / game-over check) and re-arm the clock.
            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow);
        }

        // ── Debug turn advance (kept from M1) ────────────────────────────────

        private static ValueResult<FsmState?> HandleAdvanceTurn(AlphaChainGameContext context, AdvanceTurnCommand cmd)
        {
            var turnManager = context.State.TurnManager;

            if (cmd.ActorUserId != turnManager.CurrentPlayer)
                return new ResultError("It is not your turn.",
                    $"Player [{cmd.ActorUserId}] tried to advance but the active player is [{turnManager.CurrentPlayer}].");

            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow);
        }

        // ── Shot clock ───────────────────────────────────────────────────────

        public ValueResult<FsmState?> Tick(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

            // Timer still running — nothing to do.
            if (now < state.PhaseEndTime)
                return null;

            var current = state.TurnManager.CurrentPlayer;
            state.GamePlayers.TryGetValue(current ?? string.Empty, out var player);

            if (state.Settings.SurvivalMode)
            {
                // Survival: running out the clock eliminates the current player.
                if (player is not null)
                    player.IsEliminated = true;

                context.Logger.LogDebug("Alpha Chain shot clock expired (survival) — eliminated {player}.", current);

                // If fewer than two players remain active, the match is over.
                if (CountActivePlayers(state) < 2)
                    return new GameOverState();
            }
            else
            {
                // Non-survival: the turn scores nothing and the timeout is recorded.
                if (player is not null)
                    player.TurnTimeouts++;

                context.Logger.LogDebug("Alpha Chain shot clock expired — zeroed turn for {player}.", current);
            }

            // Advance past the (now eliminated/penalized) player and re-arm the clock.
            return AdvanceTurnAndEvaluate(context, now);
        }

        // ── Turn advancement ─────────────────────────────────────────────────

        /// <summary>
        /// Advances to the next active player (skipping eliminated/left seats), applies the
        /// canonical end-of-round rule when the order wraps, and re-arms the shot clock.
        /// Returns <see cref="GameOverState"/> when the final scheduled round completes, else null.
        /// </summary>
        private static ValueResult<FsmState?> AdvanceTurnAndEvaluate(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;
            bool wrapped = AdvanceToNextActivePlayer(state);

            if (wrapped)
            {
                // The turn order wrapped — a round completed. Evaluate the canonical
                // end condition against the round that just finished.
                int completedRound = state.CurrentRound;
                int lastScheduledRound = state.Settings.EraInterval * state.Settings.EraCount;

                // Game over on the final scheduled round (no Intermission ever follows it).
                if (completedRound == lastScheduledRound)
                    return new GameOverState();

                // Era-boundary → Intermission (Rule 2) is a no-op until M4; just continue.
                state.CurrentRound++;
            }

            state.ResetTurnTimer(now);
            return null;
        }

        /// <summary>
        /// Rotates the turn to the next player who is neither eliminated nor left. Returns
        /// whether the turn order wrapped past seat 0 during the advance. If no active player
        /// is found after a full loop, leaves the turn where it landed (the caller's
        /// game-over checks handle the all-inactive case).
        /// </summary>
        private static bool AdvanceToNextActivePlayer(AlphaChainGameState state)
        {
            var turnManager = state.TurnManager;
            bool wrapped = false;

            for (int i = 0; i < turnManager.TurnOrder.Count; i++)
            {
                wrapped |= turnManager.NextTurn();

                var id = turnManager.CurrentPlayer;
                if (id is null)
                    return wrapped;

                if (state.GamePlayers.TryGetValue(id, out var ps) && !ps.IsEliminated && !ps.HasLeft)
                    return wrapped;
            }

            return wrapped;
        }

        /// <summary>Counts players still in play (not eliminated, not left).</summary>
        private static int CountActivePlayers(AlphaChainGameState state)
        {
            int count = 0;
            foreach (var ps in state.GamePlayers.Values)
                if (!ps.IsEliminated && !ps.HasLeft)
                    count++;
            return count;
        }
    }
}
