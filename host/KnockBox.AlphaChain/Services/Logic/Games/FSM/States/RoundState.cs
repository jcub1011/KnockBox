using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    using FsmState = IGameState<AlphaChainGameContext, AlphaChainCommand>;

    /// <summary>
    /// The core turn loop. Players submit words via <see cref="SubmitWordCommand"/>; the chain
    /// (succession) rule, uniqueness, dictionary validation, and the Zero-Point Tax are enforced here,
    /// while scoring and every modifier side-effect now live on the cards themselves — folded through
    /// <see cref="AlphaChainGameContext.Evaluator"/> and fired via the cards' lifecycle hooks
    /// (<c>OnWordAccepted</c>, <c>OnOpponentWordResolved</c>, <c>OnTurnEnded</c>, <c>OnValidationFailed</c>),
    /// which reach engine operations through the per-room services on the evaluation context. The shot
    /// clock is a real timer: <see cref="Tick"/> zeroes a player's turn (or eliminates them in Survival
    /// mode) when it runs out. When the turn order wraps the canonical era/round rule decides whether
    /// the game ends.
    /// </summary>
    public sealed class RoundState : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.Round);
            state.ResetTurnTimer(DateTimeOffset.UtcNow);

            state.LatestScoreReplay = null;
            state.PendingTransitionAt = null;
            state.PendingTransitionIsGameOver = false;

            BeginRound(context);

            context.Logger.LogDebug(
                "Alpha Chain FSM → RoundState (era {era}, round {round}, active {player})",
                state.CurrentEra, state.CurrentRound, state.TurnManager.CurrentPlayer);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<TimeSpan> GetRemainingTime(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

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
            // Validate-and-build → score-and-tax → record/credit → fire card reactions → publish.
            // Each stage is a behavior-preserving slice of the former monolith; the ordering here is
            // the contract. A rejection short-circuits with the typed result already stashed.
            if (!TryBuildAndValidate(context, cmd, out var submission))
                return null;

            var scored = ScoreAndResolveTax(context, submission);
            ConsumeHijackBan(submission);
            RecordPlayAndCredit(context.State, submission, scored);

            var resolution = new WordResolution(
                cmd.ActorUserId, submission.Word, submission.Taxed, scored.BaseScore, scored.Score, scored.OffendingLetter)
            {
                SiphonSuppressed = scored.SuppressBounty,
                RemainingSeconds = (int)submission.Remaining,
            };

            FireReactions(submission, resolution);
            PublishReplayAndResult(context, cmd, submission, scored);

            return AdvanceTurnAndEvaluate(context, cmd.Now, holdForReplay: true);
        }

        /// <summary>
        /// Steps 0–7: gate the submission (transition hold, active player, empty, succession,
        /// uniqueness, dictionary), build the single <see cref="EngineEvaluationContext"/> threaded
        /// through the rest of the submission, and decide whether the Zero-Point Tax applies. On any
        /// rejection it stashes the typed result on <see cref="AlphaChainGameContext.LastSubmitResult"/>
        /// and returns false (a dictionary miss still fires the owner's <c>OnValidationFailed</c> hooks).
        /// </summary>
        private static bool TryBuildAndValidate(
            AlphaChainGameContext context, SubmitWordCommand cmd, out ValidatedSubmission submission)
        {
            submission = default;
            var state = context.State;
            var turnManager = state.TurnManager;

            // 0. Holding for the round-ending word's animation — refuse further submissions.
            if (state.PendingTransitionAt is not null)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedNotYourTurn();
                return false;
            }

            // 1. Only the active player may submit.
            if (cmd.ActorUserId != turnManager.CurrentPlayer.GetValueOrDefault())
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedNotYourTurn();
                return false;
            }

            // 2. Normalize.
            string word = cmd.WordRaw.Trim().ToLowerInvariant();

            // 3. Empty after trimming.
            if (word.Length == 0)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedEmpty();
                return false;
            }

            state.GamePlayers.TryGetValue(cmd.ActorUserId, out var player);

            // Refresh the per-resolution service scratch and build the evaluation context once — the
            // single object threaded through the scoring walk and every card hook this submission.
            var services = context.EvaluationServices;
            services.BeginResolution(cmd.Now);
            var players = OrderedTurnPlayers(state);
            int playerIndex = player is null ? -1 : players.IndexOf(player);
            double remaining = Math.Max(0, (state.PhaseEndTime - cmd.Now).TotalSeconds);

            // The submitter's transient hijack ban and era-rolled card bans now live in room services,
            // not on the player. Snapshot them once for the tax check / offending-letter / context below.
            char? hijackBan = player is null ? null : services.Get<IHijackBanService>()?.Peek(player);
            IReadOnlyCollection<char> cardBans = player is null
                ? []
                : services.Get<ICardBanService>()?.BansFor(player) ?? [];

            var evalCtx = new EngineEvaluationContext(word, CollectBannedLetters(state, hijackBan, cardBans), players)
            {
                Services = services,
                PlayerIndex = playerIndex,
                SubmissionHistory = state.SubmissionHistory,
                ShotClockDuration = state.Settings.ShotClockSeconds,
                ModifiedShotClockDuration = player is null
                    ? state.Settings.ShotClockSeconds
                    : state.ComputeArmedShotClockSeconds(player),
                RemainingShotClockDuration = remaining,
            }.WithBay(player?.EngineBay ?? []);

            // 4. Chain (succession) rule — a held Wildcard exempts the owner.
            bool ignoresSuccession = player is not null && evalCtx.Bay.IgnoresSuccession(evalCtx);
            if (!ignoresSuccession && state.RequiredStartLetter is { } required && word[0] != required)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedChainBroken(required);
                return false;
            }

            // 5. Uniqueness.
            if (state.PlayedWords.Contains(word))
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedDuplicate();
                return false;
            }

            // 6. Dictionary membership. The Prism's OnValidationFailed refills the clock on a typo.
            if (!context.WordList.IsValidWord(word))
            {
                if (player is not null)
                    foreach (var card in player.EngineBay)
                        evalCtx = card.OnValidationFailed(evalCtx, card);
                context.LastSubmitResult = new SubmitWordResult.RejectedNotInDictionary();
                return false;
            }

            // 7. Zero-Point Tax: any active banned letter zeroes the score — era letter, personal hijack
            //    ban, or any era-rolled card-ban (unless a Faraday-style immunity applies).
            bool faraday = player is not null && evalCtx.Bay.ImmuneToOwnCardBans(evalCtx);
            bool taxed =
                ContainsAny(word, state.BannedLetter, hijackBan)
                || (!faraday && ContainsAnyOf(word, cardBans))
                // A card-driven legality rule (Slow Burn's 6-letter floor) taxes the word like a ban.
                || (player is not null && evalCtx.Bay.ViolatesLegalityRule(evalCtx));

            submission = new ValidatedSubmission(
                player, word, evalCtx, services, players, hijackBan, cardBans, faraday, taxed, remaining);
            return true;
        }

        /// <summary>
        /// Steps 8/8c/8d: run the scoring pipeline, then resolve the two owner-side tax rules in order
        /// — The IRS Agent's flat override (and bounty suppression) first, then Tax Write-Off's
        /// first-letter salvage on top — and capture the offending banned letter.
        /// </summary>
        private static ScoredSubmission ScoreAndResolveTax(AlphaChainGameContext context, in ValidatedSubmission v)
        {
            var state = context.State;
            var evalCtx = v.EvalCtx;

            // 8. Scoring pipeline (sequential, left → right over the bay).
            var breakdown = context.Evaluator.CalculateSteps(evalCtx, v.Taxed);
            int baseScore = breakdown.FinalBeforeTax;
            int score = breakdown.FinalScore;

            // 8c. The IRS Agent overrides the owner's own Zero-Point Tax (flat points, suppressed bounty).
            bool suppressBounty = false;
            if (v.Taxed && v.Player is not null && evalCtx.Bay.OwnTaxPolicy() is { } ownTax)
            {
                score = ownTax.GetTaxedScore(evalCtx, baseScore);
                suppressBounty = ownTax.SuppressesSiphonBounty;
                if (score > 0)
                    breakdown = breakdown with { FinalScore = score };
            }

            // 8d. Tax Write-Off: on the owner's own taxed word, salvage by scoring the first letter
            //     clean and adding it on top (the original word still scores 0 and stays siphonable).
            if (v.Taxed && v.Player is not null && evalCtx.Bay.TaxWriteOffPolicy() is { } writeOff)
            {
                int bonus = writeOff.GetWriteOffBonus(evalCtx, context.Evaluator);
                if (bonus > 0)
                {
                    score += bonus;
                    breakdown = breakdown with { FinalScore = score };
                }
            }

            // The banned letter the word used, captured before the personal ban is consumed.
            char? offendingLetter = (v.Taxed && v.Player is not null)
                ? FirstBannedLetterUsed(v.Word, state.BannedLetter, v.HijackBan, v.Faraday ? null : v.CardBans)
                : null;

            return new ScoredSubmission(score, baseScore, breakdown, suppressBounty, offendingLetter);
        }

        /// <summary>Step 9: consume the submitter's one-shot hijack ban now it has been read for the tax check.</summary>
        private static void ConsumeHijackBan(in ValidatedSubmission v)
        {
            if (v.Player is not null)
                v.Services.Get<IHijackBanService>()?.ConsumeFor(v.Player);
        }

        /// <summary>Steps 10–11: record the played word, advance the chain's required start letter, and
        /// credit the submitter their score.</summary>
        private static void RecordPlayAndCredit(AlphaChainGameState state, in ValidatedSubmission v, in ScoredSubmission scored)
        {
            state.PlayedWords.Add(v.Word);
            state.LastWord = v.Word;
            state.RequiredStartLetter =
                (state.BannedLetter is { } b && b == v.Word[^1]) ? null : v.Word[^1];

            if (v.Player is not null)
                v.Player.Score += scored.Score;
        }

        /// <summary>
        /// Steps 11a–c: fire the card lifecycle hooks for an accepted word — the owner's
        /// <c>OnWordAccepted</c>, the double-letter mark, every other active player's
        /// <c>OnOpponentWordResolved</c>, and the owner's <c>OnTurnEnded</c> automated aggression
        /// (routed through victims' interceptors). Hook side effects land on live player states and
        /// room services; the threaded evaluation context is discarded once the hooks finish.
        /// </summary>
        private static void FireReactions(in ValidatedSubmission v, WordResolution resolution)
        {
            if (v.Player is null)
                return;

            var player = v.Player;
            var services = v.Services;
            var players = v.Players;
            var evalCtx = v.EvalCtx;

            // 11a. OnWordAccepted — the owner's post-credit reactions, each routed through their cards.
            foreach (var card in player.EngineBay)
                evalCtx = card.OnWordAccepted(evalCtx, card);

            // Track a double-letter word so opponents' Scattershot can target this player this era.
            if (HasDoubleLetter(v.Word))
                services.Get<IDoubleLetterTracker>()?.Mark(player);

            // 11b. OnOpponentWordResolved — reactive economy/penalties on every OTHER active player
            //      (Tax Collector, Toll Booth, Bounty Hunter), each routed through their cards.
            foreach (var other in players)
            {
                if (other.UserId == player.UserId || other.IsEliminated || other.HasLeft)
                    continue;
                int oidx = players.IndexOf(other);
                var octx = new EngineEvaluationContext(v.Word, Array.Empty<char>(), players)
                {
                    Services = services,
                    PlayerIndex = oidx,
                    SubmissionHistory = v.EvalCtx.SubmissionHistory,
                    Resolution = resolution,
                }.WithBay(other.EngineBay);
                foreach (var card in other.EngineBay)
                    octx = card.OnOpponentWordResolved(octx, card);
            }

            // 11c. OnTurnEnded — the submitter's automated aggression (Flak Cannon time-shaves,
            //      Bait & Switch letter-hijacks), each routed through the victim's Titanium Mirror.
            evalCtx = evalCtx with { Resolution = resolution };
            foreach (var card in player.EngineBay)
                evalCtx = card.OnTurnEnded(evalCtx, card);
        }

        /// <summary>
        /// Gather the per-resolution card outputs (era-tax collectors/bounty, engine notices), append
        /// the accepted word to the match history, publish the score-replay trace, and stash the typed
        /// submit result the engine reads back to the page.
        /// </summary>
        private static void PublishReplayAndResult(
            AlphaChainGameContext context, SubmitWordCommand cmd, in ValidatedSubmission v, in ScoredSubmission scored)
        {
            var state = context.State;
            var services = v.Services;

            // Gather the per-resolution outputs the cards produced.
            IReadOnlyList<string> collectors = services.EraTaxCollectors.Count > 0
                ? services.EraTaxCollectors.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
            int bounty = services.EraTaxBounty;
            var effects = services.Notices.ToList();

            string displayName = v.Player?.DisplayName ?? cmd.ActorUserId.ToString();

            // Append the accepted submission to the match history. This single list backs the UI feed,
            // the game-over totals, and the prior-words snapshot the next submission's context reads
            // (this word was excluded from the context built above, which snapshotted before this append).
            state.SubmissionHistory = state.SubmissionHistory.Add(new AlphaChainSubmission(
                cmd.Now, cmd.ActorUserId, displayName,
                v.Word, scored.Score, v.Taxed, bounty, scored.Breakdown));

            // Publish the scoring trace + any fired effects so every client plays the replay strip.
            state.ScoreReplaySequence++;
            state.LatestScoreReplay = new ScoreReplay(
                state.ScoreReplaySequence, cmd.ActorUserId, displayName,
                scored.Breakdown, bounty, collectors, effects);
            if (effects.Count > 0)
            {
                state.LatestEngineNotices = effects;
                state.EngineNoticeSequence++;
            }

            context.LastSubmitResult = (v.Taxed && scored.Score == 0)
                ? new SubmitWordResult.AcceptedZeroPointTax()
                : new SubmitWordResult.Accepted(scored.Score);
        }

        /// <summary>A validated, ready-to-score submission: the player (null only when the actor has
        /// vanished), the normalized word, the single evaluation context, the per-room services, the
        /// turn-ordered players, the snapshotted bans, and whether the Zero-Point Tax applies.</summary>
        private readonly record struct ValidatedSubmission(
            AlphaChainPlayerState? Player,
            string Word,
            EngineEvaluationContext EvalCtx,
            AlphaChainEvaluationServices Services,
            List<AlphaChainPlayerState> Players,
            char? HijackBan,
            IReadOnlyCollection<char> CardBans,
            bool Faraday,
            bool Taxed,
            double Remaining);

        /// <summary>The scored outcome of a submission after the two owner-side tax rules resolve.</summary>
        private readonly record struct ScoredSubmission(
            int Score,
            int BaseScore,
            ScoreBreakdown Breakdown,
            bool SuppressBounty,
            char? OffendingLetter);

        // ── Banned-letter helpers ─────────────────────────────────────────────

        /// <summary>The banned letters in effect for the submitter (era + personal hijack +
        /// era-rolled card bans), surfaced on the evaluation context for cards that read them. The
        /// personal hijack and card bans are read from the room services and passed in.</summary>
        private static IReadOnlyList<char> CollectBannedLetters(
            AlphaChainGameState state, char? hijackBan, IReadOnlyCollection<char> cardBans)
        {
            var letters = new List<char>();
            if (state.BannedLetter is { } era) letters.Add(era);
            if (hijackBan is { } personal) letters.Add(personal);
            letters.AddRange(cardBans);
            return letters;
        }

        /// <summary>True when <paramref name="word"/> contains any of the supplied banned letters (nulls ignored).</summary>
        private static bool ContainsAny(string word, params char?[] bans)
        {
            foreach (var ban in bans)
                if (ban is { } c && word.Contains(c))
                    return true;
            return false;
        }

        /// <summary>True when <paramref name="word"/> contains any of <paramref name="letters"/> (null collection ignored).</summary>
        private static bool ContainsAnyOf(string word, IEnumerable<char>? letters)
        {
            if (letters is null) return false;
            foreach (char c in letters)
                if (word.Contains(c))
                    return true;
            return false;
        }

        /// <summary>The banned letter <paramref name="word"/> used, preferring the era letter, then the
        /// personal hijack ban, then any era-rolled card-ban. Backs Bait &amp; Switch's "that exact letter".</summary>
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

        /// <summary>True when two equal ASCII letters sit adjacent anywhere in the word (the 'ff' in <i>coffin</i>).</summary>
        private static bool HasDoubleLetter(string word)
        {
            for (int i = 1; i < word.Length; i++)
                if (word[i] is >= 'a' and <= 'z' && word[i] == word[i - 1])
                    return true;
            return false;
        }

        /// <summary>Players in turn order (index-aligned with the evaluation context's player indices).</summary>
        private static List<AlphaChainPlayerState> OrderedTurnPlayers(AlphaChainGameState state)
        {
            var list = new List<AlphaChainPlayerState>(state.TurnManager.TurnOrder.Count);
            foreach (var id in state.TurnManager.TurnOrder)
                if (state.GamePlayers.TryGetValue(id, out var ps))
                    list.Add(ps);
            return list;
        }

        /// <summary>Clears any active board-state at the start of a round and marks the leader the
        /// Bounty Hunter watches, then arms the opening player's turn-scoped state (e.g. the Prism's
        /// once-per-turn refill guard).</summary>
        private static void BeginRound(AlphaChainGameContext context)
        {
            var state = context.State;
            state.RoundLeaderUserId = EngineEffectResolver.LeaderUserId(state);
            BeginTurnFor(context, state.TurnManager.CurrentPlayer);
        }

        /// <summary>Fires the turn-start boundary for the now-active player, so every room state service
        /// re-arms its per-turn state (The Prism's once-per-turn refill guard).</summary>
        private static void BeginTurnFor(AlphaChainGameContext context, Guid? userId)
        {
            if (userId is { } id && context.State.GamePlayers.TryGetValue(id, out var player))
                context.EvaluationServices.FireTurnStarted(player);
        }

        // ── Debug turn advance (kept from M1) ────────────────────────────────

        private static ValueResult<FsmState?> HandleAdvanceTurn(AlphaChainGameContext context, AdvanceTurnCommand cmd)
        {
            var turnManager = context.State.TurnManager;

            if (cmd.ActorUserId != turnManager.CurrentPlayer.GetValueOrDefault())
                return new ResultError("It is not your turn.",
                    $"Player [{cmd.ActorUserId}] tried to advance but the active player is [{turnManager.CurrentPlayer}].");

            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow);
        }

        // ── Shot clock ───────────────────────────────────────────────────────

        public ValueResult<FsmState?> Tick(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

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

            if (now < state.PhaseEndTime)
                return null;

            var current = state.TurnManager.CurrentPlayer;
            state.GamePlayers.TryGetValue(current.GetValueOrDefault(), out var player);

            if (state.Settings.SurvivalMode)
            {
                if (player is not null)
                    state.MarkEliminated(player);

                context.Logger.LogDebug("Alpha Chain shot clock expired (survival) — eliminated {player}.", current);

                if (CountActivePlayers(state) < 2)
                    return new GameOverState();
            }
            else
            {
                if (player is not null)
                    player.TurnTimeouts++;

                context.Logger.LogDebug("Alpha Chain shot clock expired — zeroed turn for {player}.", current);
            }

            return AdvanceTurnAndEvaluate(context, now);
        }

        // ── Turn advancement ─────────────────────────────────────────────────

        private static ValueResult<FsmState?> AdvanceTurnAndEvaluate(
            AlphaChainGameContext context, DateTimeOffset now, bool holdForReplay = false)
        {
            var state = context.State;
            bool wrapped = AdvanceToNextActivePlayer(state);

            if (wrapped)
            {
                int completedRound = state.CurrentRound;
                int lastScheduledRound = state.Settings.EraInterval * state.Settings.EraCount;

                bool gameOver = completedRound == lastScheduledRound;
                bool eraBoundary = !gameOver && completedRound % state.Settings.EraInterval == 0;

                if (gameOver || eraBoundary)
                {
                    if (holdForReplay && state.LatestScoreReplay is { HasAnimation: true } replay)
                    {
                        state.PendingTransitionAt = now + ComputeReplayHold(replay, state.Settings);
                        state.PendingTransitionIsGameOver = gameOver;
                        return null;
                    }

                    return NextAfterRoundWrap(state, gameOver);
                }

                state.CurrentRound++;
                state.RoundLeaderUserId = EngineEffectResolver.LeaderUserId(state);
            }

            state.ResetTurnTimer(now);
            BeginTurnFor(context, state.TurnManager.CurrentPlayer);
            ApplyQueuedTimePenalty(context);
            return null;
        }

        private static ValueResult<FsmState?> NextAfterRoundWrap(AlphaChainGameState state, bool gameOver)
        {
            if (gameOver)
                return new GameOverState();

            if (state.Settings.EnableTutorials && state.CurrentEra == 1
                && !state.ShownTutorials.Contains(TutorialKind.Engine))
                return new TutorialState(TutorialKind.Engine, new IntermissionState());

            return new IntermissionState();
        }

        private static TimeSpan ComputeReplayHold(ScoreReplay replay, AlphaChainSettings settings)
        {
            int rows = replay.AnimationRows;
            double stepSeconds = Math.Min(settings.EngineAnimationSeconds / Math.Max(1, rows), 0.5);
            return TimeSpan.FromSeconds(rows * stepSeconds + 0.8);
        }

        private static void ApplyQueuedTimePenalty(AlphaChainGameContext context)
        {
            var state = context.State;
            var current = state.TurnManager.CurrentPlayer;
            if (current is null) return;

            if (state.GamePlayers.TryGetValue(current.Value, out var player))
            {
                int seconds = context.EvaluationServices.Get<ITimePenaltyService>()?.ConsumeFor(player) ?? 0;
                if (seconds > 0)
                    state.PhaseEndTime = state.PhaseEndTime.AddSeconds(-seconds);
            }
        }

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

                if (state.GamePlayers.TryGetValue(id.Value, out var ps) && !ps.IsEliminated && !ps.HasLeft)
                    return wrapped;
            }

            return wrapped;
        }

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
