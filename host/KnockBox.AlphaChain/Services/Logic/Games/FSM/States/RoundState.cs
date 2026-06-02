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
    /// Modifiers reshape scoring (via <see cref="AlphaChainGameContext.ScoreCalculator"/>), and
    /// <b>reaction cards auto-fire on game events</b> (via <see cref="ReactionResolver"/>): Amnesty
    /// at submit, the offensive/board reactions on the resulting standings swing, Free Throw at
    /// turn start, and Overtime when the shot clock expires. The shot clock is a real timer:
    /// <see cref="Tick"/> zeroes a player's turn (or eliminates them in Survival mode) when it runs
    /// out. When the turn order wraps the canonical era/round rule decides whether the game ends.
    /// </summary>
    public sealed class RoundState : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.Round);
            state.ResetTurnTimer(DateTimeOffset.UtcNow);

            // Drop the previous era's last score replay so it doesn't animate on this era's bays
            // after the Intermission deal, and clear any leftover transition hold. A Censor never
            // bleeds across an era boundary.
            state.LatestScoreReplay = null;
            state.PendingTransitionAt = null;
            state.PendingTransitionIsGameOver = false;
            ClearCensor(state);

            // The opening player of the era never goes through AdvanceToNextActivePlayer, so fire
            // their turn-start reactions (Free Throw) here too.
            ApplyTurnStartReactions(context, state);

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

            // 4. Chain (succession) rule — first letter must match the required start letter.
            //    A Free Throw reaction (if held) already cleared it at turn start when rare.
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

            // Standings before this word is credited — the reaction resolver diffs pre vs post.
            var preRanks = ReactionResolver.RankByScore(state);
            var reactions = new List<ReactionEvent>();

            // 7. Zero-Point Tax: any active banned letter anywhere in the word zeroes the score —
            //    the match's era letter, an active board-wide Censor (unless the submitter is a
            //    Riposte-exempt holder), a personal Jinx/Bait & Switch letter, or any personal
            //    card-ban the submitter rolled this era (Roulette Wheel, Smuggler's Toll).
            char? censorBan = (state.CensorBannedLetter is { } cb && !state.CensorExemptUserIds.Contains(cmd.ActorUserId))
                ? cb : null;
            bool containsBanned =
                ContainsAny(word, state.BannedLetter, censorBan, player?.PersonalBannedLetter)
                || ContainsAnyOf(word, player?.CardBannedLetters.Values);

            // 8. Full scoring pipeline: (L + ΣA) × ΠM, left → right over the bay. The WordContext is
            //    seeded with only the canonical era letter so modifier triggers (Tax Collector) and
            //    the replay's "banned" anchor stay tied to it, not to transient Censor/Jinx bans.
            //    Turn context (remaining clock, multiplier scale) feeds the time-aware/meta cards.
            double remainingSeconds = Math.Max(0, (state.PhaseEndTime - cmd.Now).TotalSeconds);
            double multiplierScale = ActiveMultiplierScale(player);
            var ctx = WordContext.Build(
                word, state.BannedLetter, remainingSeconds, state.Settings.ShotClockSeconds, multiplierScale);
            var bay = (IReadOnlyList<ModifierCard>?)player?.EngineBay ?? [];
            var probe = context.ScoreCalculator.CalculateSteps(ctx, bay, taxed: false);

            // 8b. Amnesty auto-fires (only when beneficial) to suppress the tax on a banned-letter
            //     word that would have scored. Resolved before the tax is finalized.
            bool amnestyFired = ReactionResolver.TryAmnesty(player, containsBanned, probe.FinalBeforeTax, reactions);
            bool taxed = containsBanned && !amnestyFired;
            var breakdown = taxed ? context.ScoreCalculator.CalculateSteps(ctx, bay, taxed: true) : probe;
            int baseScore = breakdown.FinalBeforeTax;
            int score = breakdown.FinalScore;

            // 8c. IRS: a held card salvages the owner's own taxed word into a flat score and (when
            //     so configured) denies the Tax Collector / Enforcer bounty. The replay strip shows
            //     the salvaged figure rather than a bare 0.
            bool suppressBounty = false;
            if (taxed && player?.EngineBay.FirstOrDefault(c => c.OwnTax is not null)?.OwnTax is { } ownTax)
            {
                score = ownTax.FlatPoints;
                suppressBounty = ownTax.SuppressesBounty;
                if (score > 0)
                    breakdown = breakdown with { FinalScore = score };
            }

            // 8d. Bait & Switch: a held card forces the offending banned letter onto the next player
            //     as a personal ban (applied when the turn advances to them).
            if (taxed && player is not null && player.EngineBay.Any(c => c.ForcesNextPlayerBan)
                && FirstBannedLetterUsed(word, state.BannedLetter, censorBan,
                       player.PersonalBannedLetter, player.CardBannedLetters.Values) is { } offending)
                state.PendingForcedPersonalBan = offending;

            // 9. The personal Jinx/Bait & Switch letter (if any) is consumed by this accepted submission.
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
            //      shot clock + doubled multipliers) for the rest of the era. Affects later turns,
            //      not this one (this word already scored under the pre-latch scale).
            TryLatchHyperDrive(state, player, remainingSeconds);

            // 11b. Siphon payouts: a taxed word feeds every other Tax Collector / Enforcer (a cut of
            //      the would-be score), and a normally-scored word that used an opponent's Smuggler's
            //      Toll letter mints that opponent a cut of what the submitter earned. The submitter
            //      never collects from their own word; IRS can suppress the taxed bounty.
            var (bounty, taxCollectors) =
                PaySiphons(state, cmd.ActorUserId, word, taxed, baseScore, score, suppressBounty);

            // 11c. Resolve the standings-driven reactions now that the score and the siphon payouts
            //      have both landed (so the pre/post diff reflects the full swing). Word length feeds
            //      the Toll Booth point-steal trigger (7+ letters).
            ReactionResolver.ResolveAfterScore(context, cmd.ActorUserId, score, word.Length, preRanks, reactions);

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
            // bounty + collector names ride along, and any reactions that fired this submission
            // ride along too (rendered as extra rows + the prominent overlay).
            state.ScoreReplaySequence++;
            state.LatestScoreReplay = new ScoreReplay(
                state.ScoreReplaySequence, cmd.ActorUserId, player?.DisplayName ?? cmd.ActorUserId,
                breakdown, bounty, taxCollectors, reactions);
            if (reactions.Count > 0)
            {
                state.LatestReactionNotices = reactions;
                state.ReactionNoticeSequence++;
            }

            // 12. Result is set before advancing so it reflects this submission. A taxed word that
            //     an IRS salvaged into points reads as a normal accept (the player did score).
            context.LastSubmitResult = (taxed && score == 0)
                ? new SubmitWordResult.AcceptedZeroPointTax()
                : new SubmitWordResult.Accepted(score);

            // 13. Advance the turn (round increment / game-over check) and re-arm the clock. An
            //     era-ending word holds in RoundState until its score animation finishes.
            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow, holdForReplay: true);
        }

        /// <summary>
        /// Pays every modifier siphon triggered by a submission and returns the era-tax bounty for
        /// the replay strip's "stolen by …" line. Two independent siphon families resolve here:
        /// <list type="bullet">
        /// <item><b>Era-tax</b> (Tax Collector / Enforcer): on a taxed word, each <i>other</i> active
        /// holder collects their single highest matching rate × <paramref name="wouldBeScore"/>
        /// (rates don't stack). Suppressed by an IRS on the submitter (<paramref name="suppressBounty"/>).</item>
        /// <item><b>Card-ban</b> (Smuggler's Toll): on a normally-scored word that used a holder's
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
        /// match <paramref name="trigger"/>, or 0 when none — so Tax Collector + Enforcer pays 75%,
        /// not 125%.</summary>
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
        /// The banned letter <paramref name="word"/> actually used, preferring the era letter, then a
        /// board-wide Censor, then the personal Jinx/Bait &amp; Switch letter, then any era-rolled
        /// card-ban. Null when the word used none. Backs Bait &amp; Switch's "that exact letter".
        /// </summary>
        private static char? FirstBannedLetterUsed(
            string word, char? eraBan, char? censorBan, char? personalBan, IEnumerable<char>? cardBans)
        {
            if (eraBan is { } e && word.Contains(e)) return e;
            if (censorBan is { } c && word.Contains(c)) return c;
            if (personalBan is { } p && word.Contains(p)) return p;
            if (cardBans is not null)
                foreach (char b in cardBans)
                    if (word.Contains(b))
                        return b;
            return null;
        }

        // ── Reaction helpers ─────────────────────────────────────────────────

        /// <summary>True when <paramref name="word"/> contains any of the supplied banned letters
        /// (nulls ignored). Backs the broadened Zero-Point Tax (era + Censor + personal Jinx).</summary>
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
        /// Fires the turn-start reactions for the now-active player (Free Throw), publishing a
        /// reaction notice if anything fired. Called from <see cref="OnEnter"/> and after each
        /// turn advance.
        /// </summary>
        private static void ApplyTurnStartReactions(AlphaChainGameContext context, AlphaChainGameState state)
        {
            var notices = new List<ReactionEvent>();
            ReactionResolver.TryFreeThrow(state, notices);
            if (notices.Count > 0)
            {
                state.LatestReactionNotices = notices;
                state.ReactionNoticeSequence++;
            }
        }

        /// <summary>Clears any active board-wide Censor ban and its exemption set.</summary>
        private static void ClearCensor(AlphaChainGameState state)
        {
            state.CensorBannedLetter = null;
            state.CensorExemptUserIds.Clear();
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

            // Overtime: a held reaction rescues the current player from the timeout by extending
            // the clock once (prevents a 0-score turn, or elimination in Survival).
            var overtimeNotices = new List<ReactionEvent>();
            if (ReactionResolver.TryOvertime(state, now, overtimeNotices))
            {
                state.LatestReactionNotices = overtimeNotices;
                state.ReactionNoticeSequence++;
                return null;
            }

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
                // A board-wide Censor lasts one full rotation: clear it once the round it was
                // imposed in has been left behind (every player got exactly one turn under it).
                if (state.CensorBannedLetter is not null && state.CurrentRound > state.CensorImposedAtRound)
                    ClearCensor(state);

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

                //   3. Otherwise continue the current era with the next round.
                state.CurrentRound++;
            }

            state.ResetTurnTimer(now);
            ApplyQueuedTimePenalty(state);
            ApplyPendingForcedBan(state);
            ApplyTurnStartReactions(context, state);
            return null;
        }

        /// <summary>
        /// Applies a pending Bait &amp; Switch ban to the now-active player (set when the previous
        /// player's word was taxed while holding the card), then clears it. Overwrites any existing
        /// personal ban; consumed by the victim's next accepted submission like a Jinx.
        /// </summary>
        private static void ApplyPendingForcedBan(AlphaChainGameState state)
        {
            if (state.PendingForcedPersonalBan is not { } letter)
                return;

            if (state.TurnManager.CurrentPlayer is { } id && state.GamePlayers.TryGetValue(id, out var player))
                player.PersonalBannedLetter = letter;
            state.PendingForcedPersonalBan = null;
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
        /// If the now-active player has attack-reaction time (Toll Booth / Frostbite) queued
        /// against them, shave it off the freshly-armed shot clock and clear the debit. Caller
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
