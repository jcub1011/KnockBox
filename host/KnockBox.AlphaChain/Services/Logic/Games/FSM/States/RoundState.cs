using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Scoring;
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
    /// Tax, and the full scoring pipeline (<c>Score = (L + ΣA) × ΠM</c>) are enforced here.
    /// M3 adds card play: modifiers reshape scoring (via <see cref="AlphaChainGameContext.ScoreCalculator"/>),
    /// and action cards queue per-submission effects (Pivot/Amnesty) or steal shot-clock time
    /// (Time Thief). The shot clock is a real timer: <see cref="Tick"/> zeroes a player's turn
    /// (or eliminates them in Survival mode) when it runs out. When the turn order wraps the
    /// canonical era/round rule decides whether the game ends.
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
                PlayActionCommand cmd => HandlePlayAction(context, cmd),
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

            // Snapshot the actor's queued action (Pivot/Amnesty) for this submission. It is
            // consumed only when the submission is accepted, so a rejected word never wastes it.
            state.GamePlayers.TryGetValue(cmd.ActorUserId, out var player);
            var pending = player?.PendingAction;
            bool pivotActive = pending == ActionKind.Pivot;
            bool amnestyActive = pending == ActionKind.Amnesty;

            // 4. Chain (succession) rule — first letter must match the required start letter.
            //    A queued Pivot clears the requirement for this submission only.
            char? effectiveRequired = pivotActive ? null : state.RequiredStartLetter;
            if (effectiveRequired is { } required && word[0] != required)
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

            // 7. Zero-Point Tax: the banned letter anywhere in the word zeroes the score,
            //    unless a queued Amnesty suppresses it for this submission.
            bool containsBanned = state.BannedLetter is { } banned && word.Contains(banned);
            bool taxed = containsBanned && !amnestyActive;

            // 8. Full scoring pipeline: (L + ΣA) × ΠM, evaluated left → right over the bay.
            var ctx = WordContext.Build(word, state.BannedLetter);
            int baseScore = context.ScoreCalculator.Calculate(
                ctx, (IReadOnlyList<ModifierCard>?)player?.EngineBay ?? []);
            int score = taxed ? 0 : baseScore;

            // 9. Consume the queued action now that the submission is committed. Pivot is
            //    always spent by the submission it was queued for; Amnesty is spent only when
            //    it actually fired (a banned letter was present), so a clean word keeps it.
            if (player is not null)
            {
                if (pivotActive || (amnestyActive && containsBanned))
                    player.PendingAction = null;
            }

            // 10. Record the play and update the chain. A banned letter *as the last letter*
            //     clears the required start letter (free choice for the next player).
            state.PlayedWords.Add(word);
            state.LastWord = word;
            state.RequiredStartLetter =
                (state.BannedLetter is { } b && b == word[^1]) ? null : word[^1];

            // 11. Credit the player.
            if (player is not null)
                player.Score += score;

            // 11b. Tax Collector bounty: a taxed (banned-letter) word pays nothing to the
            //      submitter, but every *other* active player holding a Tax Collector collects
            //      half of what the word would have scored. The submitter never collects from
            //      their own taxed word.
            int bounty = PayTaxCollectorBounty(state, cmd.ActorUserId, taxed, baseScore);

            // Log the accepted play for the UI feed.
            state.PlayLog.Add(new AlphaChainWordPlay(
                DateTimeOffset.UtcNow,
                cmd.ActorUserId,
                player?.DisplayName ?? cmd.ActorUserId,
                word,
                score,
                taxed,
                bounty));

            // 12. Result is set before advancing so it reflects this submission.
            context.LastSubmitResult = taxed
                ? new SubmitWordResult.AcceptedZeroPointTax()
                : new SubmitWordResult.Accepted(score);

            // 13. Advance the turn (round increment / game-over check) and re-arm the clock.
            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Pays the Tax Collector bounty for a submission. When <paramref name="taxed"/> is true,
        /// every active player other than the submitter who holds a Tax Collector in their Engine
        /// Bay collects <see cref="ModifierLibrary.TaxCollectorRate"/> × <paramref name="wouldBeScore"/>
        /// (rounded half-up, clamped to <see cref="ScoreCalculator.MaxWordScore"/>). Returns the
        /// per-owner bounty (0 when nothing was paid). Runs inside the execute lock.
        /// </summary>
        private static int PayTaxCollectorBounty(
            AlphaChainGameState state, string submitterUserId, bool taxed, int wouldBeScore)
        {
            if (!taxed || wouldBeScore <= 0)
                return 0;

            int bounty = Math.Clamp(
                (int)Math.Round(wouldBeScore * ModifierLibrary.TaxCollectorRate, MidpointRounding.AwayFromZero),
                0, ScoreCalculator.MaxWordScore);
            if (bounty == 0)
                return 0;

            bool anyPaid = false;
            foreach (var other in state.GamePlayers.Values)
            {
                if (other.UserId == submitterUserId || other.IsEliminated || other.HasLeft)
                    continue;
                if (other.EngineBay.Any(c => c.Id == ModifierLibrary.TaxCollectorId))
                {
                    other.Score += bounty;
                    anyPaid = true;
                }
            }

            return anyPaid ? bounty : 0;
        }

        // ── Action cards ─────────────────────────────────────────────────────

        /// <summary>
        /// Plays an action card from the actor's hand. Pivot/Amnesty queue a one-shot effect
        /// for the actor's next submission (valid only on their own turn, before they submit);
        /// Time Thief steals 5 s from a target opponent's shot clock — applied immediately when
        /// the target is the active player, otherwise queued for the target's next turn.
        /// </summary>
        private static ValueResult<FsmState?> HandlePlayAction(AlphaChainGameContext context, PlayActionCommand cmd)
        {
            var state = context.State;

            // Possession: the actor must actually hold the named card.
            if (!state.GamePlayers.TryGetValue(cmd.ActorUserId, out var actor))
                return new ResultError("You are not in this game.",
                    $"PlayActionCommand from unknown player [{cmd.ActorUserId}].");

            var card = actor.ActionHand.FirstOrDefault(c => c.Id == cmd.CardId);
            if (card is null)
                return new ResultError("You don't hold that card.",
                    $"Player [{cmd.ActorUserId}] tried to play missing action card [{cmd.CardId}].");

            switch (card.Kind)
            {
                case ActionKind.Pivot:
                case ActionKind.Amnesty:
                    // Timing: queueable only on your own turn (a turn you have not yet ended by
                    // submitting — submitting advances the turn, so "your turn" implies that).
                    if (cmd.ActorUserId != state.TurnManager.CurrentPlayer)
                        return new ResultError("You can only queue that on your turn.",
                            $"Player [{cmd.ActorUserId}] tried to queue {card.Kind} out of turn.");

                    actor.PendingAction = card.Kind;
                    break;

                case ActionKind.TimeThief:
                    var thiefResult = ApplyTimeThief(context, cmd, actor);
                    if (thiefResult.TryGetFailure(out var thiefError))
                        return thiefError;
                    break;
            }

            // Spend the card (remove a single instance — hands may hold duplicates).
            actor.ActionHand.Remove(card);

            context.Logger.LogDebug(
                "Alpha Chain action [{card}] played by [{actor}] (target [{target}]).",
                card.Id, cmd.ActorUserId, cmd.TargetUserId ?? "—");

            return (ValueResult<FsmState?>)null!;
        }

        /// <summary>
        /// Applies a Time Thief play. The target must be a different, present player. When the
        /// target is the active player and their clock is still running, 5 s is shaved off
        /// <c>PhaseEndTime</c> directly; otherwise the debit is queued and applied the next
        /// time the target takes a turn. The FSM (running inside the execute lock) is the
        /// single writer of <c>PhaseEndTime</c>, so this is safe against concurrent ticks.
        /// </summary>
        private static Result ApplyTimeThief(AlphaChainGameContext context, PlayActionCommand cmd, AlphaChainPlayerState actor)
        {
            var state = context.State;

            if (cmd.TargetUserId is null)
                return Result.FromError("Time Thief needs a target.", "PlayActionCommand.TargetUserId was null.");

            if (cmd.TargetUserId == cmd.ActorUserId)
                return Result.FromError("You can't target yourself.",
                    $"Player [{cmd.ActorUserId}] aimed Time Thief at themselves.");

            if (!state.GamePlayers.TryGetValue(cmd.TargetUserId, out var target)
                || target.IsEliminated || target.HasLeft)
                return Result.FromError("That opponent isn't available.",
                    $"Time Thief target [{cmd.TargetUserId}] is unknown or out of play.");

            const int stealSeconds = 5;
            var now = DateTimeOffset.UtcNow;

            if (cmd.TargetUserId == state.TurnManager.CurrentPlayer && state.PhaseEndTime > now)
            {
                // The target is on the clock right now — steal the time immediately.
                state.PhaseEndTime = state.PhaseEndTime.AddSeconds(-stealSeconds);
            }
            else
            {
                // The target isn't currently on the clock — debit their next turn instead.
                target.QueuedTimePenaltySeconds += stealSeconds;
            }

            return Result.Success;
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
                    state.MarkEliminated(player);

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
        /// canonical end-of-round rule when the order wraps, re-arms the shot clock, and debits
        /// any Time Thief time queued against the new active player.
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

                // Canonical era/round rule (defined in M1, evaluated at the wrap point):
                //   1. Game over on the final scheduled round — no Intermission ever follows it.
                if (completedRound == lastScheduledRound)
                    return new GameOverState();

                //   2. Era boundary → Intermission. IntermissionState advances CurrentEra and
                //      bumps CurrentRound on its way back to RoundState, so we do NOT increment
                //      CurrentRound here.
                if (completedRound % state.Settings.EraInterval == 0)
                    return new IntermissionState();

                //   3. Otherwise continue the current era with the next round.
                state.CurrentRound++;
            }

            state.ResetTurnTimer(now);
            ApplyQueuedTimePenalty(state);
            return null;
        }

        /// <summary>
        /// If the now-active player has Time Thief time queued against them, shave it off the
        /// freshly-armed shot clock and clear the debit. Caller already holds the execute lock.
        /// </summary>
        private static void ApplyQueuedTimePenalty(AlphaChainGameState state)
        {
            var current = state.TurnManager.CurrentPlayer;
            if (current is null) return;

            if (state.GamePlayers.TryGetValue(current, out var player) && player.QueuedTimePenaltySeconds > 0)
            {
                state.PhaseEndTime = state.PhaseEndTime.AddSeconds(-player.QueuedTimePenaltySeconds);
                player.QueuedTimePenaltySeconds = 0;
            }
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
