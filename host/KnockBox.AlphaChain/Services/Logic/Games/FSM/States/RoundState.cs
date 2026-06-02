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
    /// Modifiers reshape scoring (via <see cref="AlphaChainGameContext.ScoreCalculator"/>) and
    /// also drive <b>automated, rule-driven engine effects</b> (via <see cref="EngineEffectResolver"/>):
    /// Flak Cannon / Scattershot time-shaves, the Bounty Hunter's leader drain, and Tracer Round /
    /// Bait &amp; Switch letter-hijacks all fire after an accepted word, with no manual targeting and
    /// no opponent-UI disruption. The shot clock is a real timer: <see cref="Tick"/> zeroes a
    /// player's turn (or eliminates them in Survival mode) when it runs out. When the turn order
    /// wraps the canonical era/round rule decides whether the game ends.
    /// </summary>
    public sealed class RoundState : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.Round);
            state.ResetTurnTimer(DateTimeOffset.UtcNow);

            // Drop the previous era's last score replay so it doesn't animate on this era's bays
            // after the Intermission deal, and clear any leftover transition hold.
            state.LatestScoreReplay = null;
            state.PendingTransitionAt = null;
            state.PendingTransitionIsGameOver = false;

            // Open the era's first round: mark the Bounty Hunter's leader and arm the opening
            // player's once-per-turn Prism flag.
            BeginRound(state);

            context.Logger.LogDebug(
                "Alpha Chain FSM → RoundState (era {era}, round {round}, active {player})",
                state.CurrentEra, state.CurrentRound, state.TurnManager.CurrentPlayer);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<TimeSpan> GetRemainingTime(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

            // While holding for the score animation, schedule the next tick for the hold deadline
            // (not the now-stale shot clock) so the transition fires right when it elapses.
            if (state.PendingTransitionAt is { } holdUntil)
            {
                var hold = holdUntil - now;
                return hold > TimeSpan.Zero ? hold : TimeSpan.Zero;
            }

            var remaining = state.PhaseEndTime - now;
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

            // 0. The round-ending word has been played; we're holding for its score animation.
            //    Refuse further submissions until the transition (Intermission/GameOver) fires.
            if (state.PendingTransitionAt is not null)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedNotYourTurn();
                return null;
            }

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

            state.GamePlayers.TryGetValue(cmd.ActorUserId, out var player);

            // 4. Chain (succession) rule — first letter must match the required start letter. A
            //    held Wildcard lets the owner ignore the requirement entirely.
            bool ignoresSuccession = player is not null && player.EngineBay.Any(c => c.IgnoresSuccessionRule);
            if (!ignoresSuccession && state.RequiredStartLetter is { } required && word[0] != required)
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

            // 6. Dictionary membership (full dictionary; non-ASCII/non-letters → false). The Prism
            //    refills the owner's shot clock to full (once per turn) on a typo/invalid word so a
            //    blind miss doesn't burn the turn — its essential pairing with The Blindfold.
            if (!context.WordList.IsValidWord(word))
            {
                TryPrismRefill(state, player, cmd.Now);
                context.LastSubmitResult = new SubmitWordResult.RejectedNotInDictionary();
                return null;
            }

            var effects = new List<EngineEffectEvent>();

            // 7. Zero-Point Tax: any active banned letter anywhere in the word zeroes the score — the
            //    match's era letter, a personal hijack ban (Tracer Round / Bait & Switch), or any
            //    era-rolled personal card-ban (Roulette Wheel, The Toll Booth) unless The Faraday
            //    Cage grants immunity to the owner's own card-bans.
            bool faraday = player is not null && player.EngineBay.Any(c => c.ImmuneToOwnCardBans);
            bool containsBanned =
                ContainsAny(word, state.BannedLetter, player?.PersonalBannedLetter)
                || (!faraday && ContainsAnyOf(word, player?.CardBannedLetters.Values));

            // 8. Full scoring pipeline: (L + ΣA) × ΠM, left → right over the bay. Turn context feeds
            //    the time-aware/meta cards: remaining clock (Sprinter/Panic/Adrenaline), Hyper-Drive
            //    scale, the live Titanium Mirror factor, and The Catalyst's Y/W/H ambiguity.
            double remainingSeconds = Math.Max(0, (state.PhaseEndTime - cmd.Now).TotalSeconds);
            double multiplierScale = ActiveMultiplierScale(player);
            double shieldMultiplier = player?.ShieldMultiplier ?? 1.0;
            bool catalyst = player is not null && player.EngineBay.Any(c => c.CatalystAmbiguousLetters);
            var ctx = WordContext.Build(
                word, state.BannedLetter, remainingSeconds, state.Settings.ShotClockSeconds,
                multiplierScale, shieldMultiplier, catalyst);
            var bay = (IReadOnlyList<ModifierCard>?)player?.EngineBay ?? [];
            var probe = context.ScoreCalculator.CalculateSteps(ctx, bay, taxed: false);

            bool taxed = containsBanned;
            var breakdown = taxed ? context.ScoreCalculator.CalculateSteps(ctx, bay, taxed: true) : probe;
            int baseScore = breakdown.FinalBeforeTax;
            int score = breakdown.FinalScore;

            // 8c. The IRS Agent: a held card overrides the owner's own Zero-Point Tax — scoring the
            //     card's flat points (0 by default) and denying the Tax Collector bounty.
            bool suppressBounty = false;
            if (taxed && player?.EngineBay.FirstOrDefault(c => c.OwnTax is not null)?.OwnTax is { } ownTax)
            {
                score = ownTax.FlatPoints;
                suppressBounty = ownTax.SuppressesBounty;
                if (score > 0)
                    breakdown = breakdown with { FinalScore = score };
            }

            // The banned letter the word actually used, captured before the personal ban is consumed
            // below — backs Bait & Switch's "that exact letter" hijack.
            char? offendingLetter = (taxed && player is not null)
                ? FirstBannedLetterUsed(word, state.BannedLetter, player.PersonalBannedLetter,
                    faraday ? null : player.CardBannedLetters.Values)
                : null;

            // 9. The submitter's transient personal ban (if any) is consumed by this accepted submission.
            if (player is not null)
                player.PersonalBannedLetter = null;

            // 10. Record the play and update the chain. A banned letter *as the last letter*
            //     clears the required start letter (free choice for the next player).
            state.PlayedWords.Add(word);
            state.LastWord = word;
            state.RequiredStartLetter =
                (state.BannedLetter is { } b && b == word[^1]) ? null : word[^1];

            // 11. Credit the player.
            if (player is not null)
                player.Score += score;

            // 11a. Hyper-Drive: a fast accepted submission latches the era-scoped overdrive (a 5s
            //      shot clock + doubled multipliers) for the rest of the era. Affects later turns.
            TryLatchHyperDrive(state, player, remainingSeconds);

            // Track a double-letter word so opponents' Scattershot can target this player this era.
            if (player is not null && ctx.HasDoubleLetter)
                player.PlayedDoubleLetterWordThisEra = true;

            // 11b. Siphon payouts: a taxed word feeds every other Tax Collector (a cut of the
            //      would-be score), and a normally-scored word that used an opponent's Toll Booth
            //      letter mints that opponent a cut of what the submitter earned. The submitter never
            //      collects from their own word; The IRS Agent can suppress the taxed bounty.
            var (bounty, taxCollectors) =
                PaySiphons(state, cmd.ActorUserId, word, taxed, baseScore, score, suppressBounty);

            // 11c. Automated aggression — all rule-driven, no targeting. Bounty Hunter docks the
            //      marked leader on a short word; Flak Cannon / Scattershot queue time-shaves; Tracer
            //      Round / Bait & Switch hijack the next player's start. Each routes through the
            //      victim's Titanium Mirror, which can block and reflect it back at its source.
            if (player is not null)
            {
                EngineEffectResolver.ResolveBountyHunter(state, player, word.Length, effects);
                EngineEffectResolver.ResolveAutoTimeShaves(state, player, effects);
                ResolveLetterHijacks(state, player, word, offendingLetter, effects);
            }

            // Log the accepted play for the UI feed.
            state.PlayLog.Add(new AlphaChainWordPlay(
                DateTimeOffset.UtcNow,
                cmd.ActorUserId,
                player?.DisplayName ?? cmd.ActorUserId,
                word,
                score,
                taxed,
                bounty));

            // Publish the scoring trace so every client plays the replay strip. The sequence bump
            // makes the strip remount and animate exactly once for this word. On a taxed word the
            // bounty + collector names ride along, and any engine effects that fired this submission
            // ride along too (rendered as extra rows + the prominent overlay).
            state.ScoreReplaySequence++;
            state.LatestScoreReplay = new ScoreReplay(
                state.ScoreReplaySequence, cmd.ActorUserId, player?.DisplayName ?? cmd.ActorUserId,
                breakdown, bounty, taxCollectors, effects);
            if (effects.Count > 0)
            {
                state.LatestEngineNotices = effects;
                state.EngineNoticeSequence++;
            }

            // 12. Result is set before advancing so it reflects this submission. A taxed word that
            //     an IRS Agent salvaged into points reads as a normal accept (the player did score).
            context.LastSubmitResult = (taxed && score == 0)
                ? new SubmitWordResult.AcceptedZeroPointTax()
                : new SubmitWordResult.Accepted(score);

            // 13. Advance the turn (round increment / game-over check) and re-arm the clock. An
            //     era-ending word holds in RoundState until its score animation finishes.
            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow, holdForReplay: true);
        }

        /// <summary>
        /// Fires the submitter's letter-hijack modifiers at the next active player: Tracer Round
        /// forces the word's ending letter, and Bait &amp; Switch forces the banned letter a taxed
        /// word used (<paramref name="offendingLetter"/>). Both route through the next player's
        /// Titanium Mirror, which may reflect the curse back onto the submitter.
        /// </summary>
        private static void ResolveLetterHijacks(AlphaChainGameState state, AlphaChainPlayerState player,
            string word, char? offendingLetter, List<EngineEffectEvent> effects)
        {
            var next = PeekNextActivePlayer(state, player.UserId);
            if (next is null)
                return;

            if (player.EngineBay.FirstOrDefault(c => c.HijacksEndLetter) is { } tracer)
                EngineEffectResolver.FireLetterHijack(tracer, player, next, word[^1], effects);

            if (offendingLetter is { } offending
                && player.EngineBay.FirstOrDefault(c => c.ForcesNextPlayerBan) is { } bait)
                EngineEffectResolver.FireLetterHijack(bait, player, next, offending, effects);
        }

        /// <summary>
        /// Pays every modifier siphon triggered by a submission and returns the era-tax bounty for
        /// the replay strip's "stolen by …" line. Two independent siphon families resolve here:
        /// <list type="bullet">
        /// <item><b>Era-tax</b> (Tax Collector): on a taxed word, each <i>other</i> active holder
        /// collects their single highest matching rate × <paramref name="wouldBeScore"/> (rates don't
        /// stack). Suppressed by an IRS Agent on the submitter (<paramref name="suppressBounty"/>).</item>
        /// <item><b>Card-ban</b> (The Toll Booth): on a normally-scored word that used a holder's
        /// era-rolled personal card-ban, that holder is minted their rate × <paramref name="earnedScore"/>;
        /// the submitter keeps their points.</item>
        /// </list>
        /// The two are mutually exclusive per word (a taxed word earns 0). Runs inside the execute lock.
        /// </summary>
        private static (int Bounty, IReadOnlyList<string> Collectors) PaySiphons(
            AlphaChainGameState state, string submitterUserId, string word,
            bool taxed, int wouldBeScore, int earnedScore, bool suppressBounty)
        {
            var collectors = new List<string>();
            int reportBounty = 0;

            if (taxed && !suppressBounty && wouldBeScore > 0)
            {
                foreach (var other in state.GamePlayers.Values)
                {
                    if (other.UserId == submitterUserId || other.IsEliminated || other.HasLeft)
                        continue;
                    double rate = MaxSiphonRate(other, SiphonTrigger.OpponentEraTaxed);
                    if (rate <= 0)
                        continue;
                    int amount = ClampScore(wouldBeScore * rate);
                    if (amount <= 0)
                        continue;
                    other.Score += amount;
                    collectors.Add(other.DisplayName);
                    reportBounty = Math.Max(reportBounty, amount);
                }
            }

            if (!taxed && earnedScore > 0)
            {
                foreach (var other in state.GamePlayers.Values)
                {
                    if (other.UserId == submitterUserId || other.IsEliminated || other.HasLeft)
                        continue;
                    foreach (var card in other.EngineBay)
                        if (card.Siphon is { Trigger: SiphonTrigger.OpponentUsedMyCardBan } s
                            && other.CardBannedLetters.TryGetValue(card.Id, out var banned)
                            && word.Contains(banned))
                        {
                            int amount = ClampScore(earnedScore * s.Rate);
                            if (amount > 0)
                                other.Score += amount;
                        }
                }
            }

            if (collectors.Count == 0)
                return (0, []);

            collectors.Sort(StringComparer.OrdinalIgnoreCase);
            return (reportBounty, collectors);
        }

        /// <summary>The single highest siphon rate among <paramref name="player"/>'s bay cards that
        /// match <paramref name="trigger"/>, or 0 when none. Kept as a max (not a sum) so future
        /// stacking siphons can't compound.</summary>
        private static double MaxSiphonRate(AlphaChainPlayerState player, SiphonTrigger trigger)
        {
            double max = 0;
            foreach (var card in player.EngineBay)
                if (card.Siphon is { } s && s.Trigger == trigger && s.Rate > max)
                    max = s.Rate;
            return max;
        }

        /// <summary>Rounds half-up and clamps a siphon payout into the legal score range.</summary>
        private static int ClampScore(double value) =>
            Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, ScoreCalculator.MaxWordScore);

        /// <summary>
        /// The banned letter <paramref name="word"/> actually used, preferring the era letter, then
        /// the personal Tracer/Bait letter, then any era-rolled card-ban. Null when the word used
        /// none. Backs Bait &amp; Switch's "that exact letter".
        /// </summary>
        private static char? FirstBannedLetterUsed(
            string word, char? eraBan, char? personalBan, IEnumerable<char>? cardBans)
        {
            if (eraBan is { } e && word.Contains(e)) return e;
            if (personalBan is { } p && word.Contains(p)) return p;
            if (cardBans is not null)
                foreach (char b in cardBans)
                    if (word.Contains(b))
                        return b;
            return null;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>True when <paramref name="word"/> contains any of the supplied banned letters
        /// (nulls ignored). Backs the Zero-Point Tax (era + personal hijack bans).</summary>
        private static bool ContainsAny(string word, params char?[] bans)
        {
            foreach (var ban in bans)
                if (ban is { } c && word.Contains(c))
                    return true;
            return false;
        }

        /// <summary>True when <paramref name="word"/> contains any of <paramref name="letters"/>
        /// (null collection ignored). Backs the era-rolled personal card-bans in the Zero-Point Tax.</summary>
        private static bool ContainsAnyOf(string word, IEnumerable<char>? letters)
        {
            if (letters is null) return false;
            foreach (char c in letters)
                if (word.Contains(c))
                    return true;
            return false;
        }

        /// <summary>
        /// The multiplier scale to seed this submission's <see cref="WordContext"/> with: the
        /// Hyper-Drive factor when the submitter has latched it this era, else 1.0 (no scaling).
        /// </summary>
        private static double ActiveMultiplierScale(AlphaChainPlayerState? player)
        {
            if (player is { HyperDriveActive: true })
                foreach (var card in player.EngineBay)
                    if (card.Hyperdrive is { } hd)
                        return hd.MultiplierScale;
            return 1.0;
        }

        /// <summary>
        /// Latches Hyper-Drive for the rest of the era when the submitter holds the card, has not
        /// already latched, and beat its threshold (elapsed = armed clock − seconds remaining at
        /// submit). The latch is read by <c>ComputeArmedShotClockSeconds</c> (clock override) and
        /// <see cref="ActiveMultiplierScale"/> (doubled multipliers) on subsequent turns.
        /// </summary>
        private static void TryLatchHyperDrive(AlphaChainGameState state, AlphaChainPlayerState? player, double remainingSeconds)
        {
            if (player is null || player.HyperDriveActive)
                return;

            var card = player.EngineBay.FirstOrDefault(c => c.Hyperdrive is not null);
            if (card?.Hyperdrive is not { } hd)
                return;

            double elapsed = state.ComputeArmedShotClockSeconds(player) - remainingSeconds;
            if (elapsed < hd.ThresholdSeconds)
                player.HyperDriveActive = true;
        }

        /// <summary>
        /// The Prism: on a typo/invalid submission, refills the owner's shot clock to a freshly-armed
        /// full clock — once per turn — instead of letting it tick down. The once-per-turn flag is
        /// cleared whenever a new turn arms for the player (see <see cref="BeginTurnFor"/>).
        /// </summary>
        private static void TryPrismRefill(AlphaChainGameState state, AlphaChainPlayerState? player, DateTimeOffset now)
        {
            if (player is null || player.PrismUsedThisTurn)
                return;
            if (!player.EngineBay.Any(c => c.RefillsClockOnFailedValidation))
                return;

            state.PhaseEndTime = now.AddSeconds(state.ComputeArmedShotClockSeconds(player));
            player.PrismUsedThisTurn = true;
        }

        /// <summary>Clears any active board-state at the start of a round and marks the leader the
        /// Bounty Hunter watches, then arms the opening player's once-per-turn Prism flag.</summary>
        private static void BeginRound(AlphaChainGameState state)
        {
            state.RoundLeaderUserId = EngineEffectResolver.LeaderUserId(state);
            BeginTurnFor(state, state.TurnManager.CurrentPlayer);
        }

        /// <summary>Resets the per-turn state for the now-active player (currently The Prism's
        /// once-per-turn refill flag).</summary>
        private static void BeginTurnFor(AlphaChainGameState state, string? userId)
        {
            if (userId is not null && state.GamePlayers.TryGetValue(userId, out var player))
                player.PrismUsedThisTurn = false;
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

            // Holding after the round-ending word so the inline score animation can finish. Once
            // the hold elapses, fire the pending transition (Intermission or GameOver). The shot
            // clock is ignored while holding.
            if (state.PendingTransitionAt is { } holdUntil)
            {
                if (now >= holdUntil)
                {
                    bool gameOver = state.PendingTransitionIsGameOver;
                    state.PendingTransitionAt = null;
                    state.PendingTransitionIsGameOver = false;
                    return NextAfterRoundWrap(state, gameOver);
                }
                return null;
            }

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
        /// any time queued against the new active player by a Flak Cannon / Scattershot shave.
        /// Returns <see cref="GameOverState"/> when the final scheduled round completes, else null.
        /// </summary>
        /// <param name="holdForReplay">
        /// True when this advance follows an accepted word submission. On a round boundary that ends
        /// the era or the match, the transition (Intermission or GameOver) is then deferred — the
        /// FSM holds in <see cref="RoundState"/> — so the inline score animation for the round-ending
        /// word can finish first. False for timeouts and debug advances, which transition immediately
        /// (no word to animate).
        /// </param>
        private static ValueResult<FsmState?> AdvanceTurnAndEvaluate(
            AlphaChainGameContext context, DateTimeOffset now, bool holdForReplay = false)
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
                //   2. Otherwise an era boundary → Intermission. IntermissionState advances
                //      CurrentEra and bumps CurrentRound on its way back, so we do NOT do it here.
                bool gameOver = completedRound == lastScheduledRound;
                bool eraBoundary = !gameOver && completedRound % state.Settings.EraInterval == 0;

                if (gameOver || eraBoundary)
                {
                    // If the round-ending word produced a score animation, hold in RoundState until
                    // it finishes (blocking further play) instead of cutting straight to the next
                    // screen. Tick fires the pending transition once the hold elapses.
                    if (holdForReplay && state.LatestScoreReplay is { HasAnimation: true } replay)
                    {
                        state.PendingTransitionAt = now + ComputeReplayHold(replay, state.Settings);
                        state.PendingTransitionIsGameOver = gameOver;
                        return null;
                    }

                    return NextAfterRoundWrap(state, gameOver);
                }

                //   3. Otherwise continue the current era with the next round, and re-mark the
                //      Bounty Hunter's leader for the fresh round.
                state.CurrentRound++;
                state.RoundLeaderUserId = EngineEffectResolver.LeaderUserId(state);
            }

            state.ResetTurnTimer(now);
            BeginTurnFor(state, state.TurnManager.CurrentPlayer);
            ApplyQueuedTimePenalty(state);
            return null;
        }

        /// <summary>
        /// Resolves the state to enter once the turn order wraps on an era boundary (or the final
        /// round). Recomputed from live state at the moment the transition fires — including the
        /// deferred score-replay-hold path — so it never relies on a stored target. When tutorials
        /// are enabled, the Engine tutorial is inserted before the FIRST Intermission only (era 1);
        /// <c>CurrentEra</c> only advances inside <c>CompleteIntermission</c>, so it is still 1 at
        /// both fire points, and the shown-flag is a redundant backstop against re-entry.
        /// </summary>
        private static ValueResult<FsmState?> NextAfterRoundWrap(AlphaChainGameState state, bool gameOver)
        {
            if (gameOver)
                return new GameOverState();

            if (state.Settings.EnableTutorials && state.CurrentEra == 1
                && !state.ShownTutorials.Contains(TutorialKind.Engine))
                return new TutorialState(TutorialKind.Engine, new IntermissionState());

            return new IntermissionState();
        }

        /// <summary>
        /// How long to hold in <see cref="RoundState"/> after the round-ending word so its inline
        /// score animation can play out. Mirrors the UI's per-step timing (total ÷ rows, capped at
        /// 0.5 s/step — see <c>ScoreReplayStrip</c>) over the reveal rows (seed + one per card + final,
        /// plus the steal line when present), plus a short read before the cut to the next screen.
        /// </summary>
        private static TimeSpan ComputeReplayHold(ScoreReplay replay, AlphaChainSettings settings)
        {
            int rows = replay.AnimationRows;
            double stepSeconds = Math.Min(settings.EngineAnimationSeconds / Math.Max(1, rows), 0.5);
            return TimeSpan.FromSeconds(rows * stepSeconds + 0.8);
        }

        /// <summary>
        /// If the now-active player has automated time-shave seconds (Flak Cannon / Scattershot)
        /// queued against them, shave it off the freshly-armed shot clock and clear the debit. Caller
        /// already holds the execute lock.
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

        /// <summary>
        /// The next active player after <paramref name="fromUserId"/> in turn order (skipping
        /// eliminated/left seats), without mutating the turn manager — used to resolve a letter
        /// hijack against whoever plays next. Null when no other active player exists.
        /// </summary>
        private static AlphaChainPlayerState? PeekNextActivePlayer(AlphaChainGameState state, string fromUserId)
        {
            var order = state.TurnManager.TurnOrder;
            int start = order.IndexOf(fromUserId);
            if (start < 0 || order.Count == 0)
                return null;

            for (int i = 1; i <= order.Count; i++)
            {
                var id = order[(start + i) % order.Count];
                if (id == fromUserId)
                    break;
                if (state.GamePlayers.TryGetValue(id, out var ps) && !ps.IsEliminated && !ps.HasLeft)
                    return ps;
            }
            return null;
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
