using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
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
            var state = context.State;
            var turnManager = state.TurnManager;

            // 0. Holding for the round-ending word's animation — refuse further submissions.
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

            // 2. Normalize.
            string word = cmd.WordRaw.Trim().ToLowerInvariant();

            // 3. Empty after trimming.
            if (word.Length == 0)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedEmpty();
                return null;
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
                Bay = player?.EngineBay ?? [],
                Services = services,
                PlayerIndex = playerIndex,
                ShotClockDuration = state.Settings.ShotClockSeconds,
                ModifiedShotClockDuration = player is null
                    ? state.Settings.ShotClockSeconds
                    : state.ComputeArmedShotClockSeconds(player),
                RemainingShotClockDuration = remaining,
                Score = player?.Score ?? 0,
            };

            // 4. Chain (succession) rule — a held Wildcard exempts the owner.
            bool ignoresSuccession = player is not null && evalCtx.Bay.IgnoresSuccession(evalCtx);
            if (!ignoresSuccession && state.RequiredStartLetter is { } required && word[0] != required)
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedChainBroken(required);
                return null;
            }

            // 5. Uniqueness.
            if (state.PlayedWords.Contains(word))
            {
                context.LastSubmitResult = new SubmitWordResult.RejectedDuplicate();
                return null;
            }

            // 6. Dictionary membership. The Prism's OnValidationFailed refills the clock on a typo.
            if (!context.WordList.IsValidWord(word))
            {
                if (player is not null)
                    foreach (var card in player.EngineBay)
                        evalCtx = card.OnValidationFailed(evalCtx, card);
                context.LastSubmitResult = new SubmitWordResult.RejectedNotInDictionary();
                return null;
            }

            // 7. Zero-Point Tax: any active banned letter zeroes the score — era letter, personal hijack
            //    ban, or any era-rolled card-ban (unless a Faraday-style immunity applies).
            bool faraday = player is not null && evalCtx.Bay.ImmuneToOwnCardBans(evalCtx);
            bool taxed =
                ContainsAny(word, state.BannedLetter, hijackBan)
                || (!faraday && ContainsAnyOf(word, cardBans));

            // 8. Scoring pipeline (sequential, left → right over the bay).
            var breakdown = context.Evaluator.CalculateSteps(evalCtx, taxed);
            int baseScore = breakdown.FinalBeforeTax;
            int score = breakdown.FinalScore;

            // 8c. The IRS Agent overrides the owner's own Zero-Point Tax (flat points, suppressed bounty).
            bool suppressBounty = false;
            if (taxed && player is not null && evalCtx.Bay.OwnTaxPolicy() is { } ownTax)
            {
                score = ownTax.GetTaxedScore(evalCtx, baseScore);
                suppressBounty = ownTax.SuppressesSiphonBounty;
                if (score > 0)
                    breakdown = breakdown with { FinalScore = score };
            }

            // The banned letter the word used, captured before the personal ban is consumed.
            char? offendingLetter = (taxed && player is not null)
                ? FirstBannedLetterUsed(word, state.BannedLetter, hijackBan,
                    faraday ? null : cardBans)
                : null;

            // 9. Consume the submitter's transient hijack ban.
            if (player is not null)
                services.Get<IHijackBanService>()?.ConsumeFor(player);

            // 10. Record the play and update the chain.
            state.PlayedWords.Add(word);
            state.LastWord = word;
            state.RequiredStartLetter =
                (state.BannedLetter is { } b && b == word[^1]) ? null : word[^1];

            // 11. Credit the player.
            if (player is not null)
                player.Score += score;

            var resolution = new WordResolution(cmd.ActorUserId, word, taxed, baseScore, score, offendingLetter)
            {
                SiphonSuppressed = suppressBounty,
            };

            if (player is not null)
            {
                // 11a. OnWordAccepted — Hyper-Drive latches the era overdrive on a fast submission.
                foreach (var card in player.EngineBay)
                    evalCtx = card.OnWordAccepted(evalCtx, card);

                // Track a double-letter word so opponents' Scattershot can target this player this era.
                if (HasDoubleLetter(word))
                    services.Get<IDoubleLetterTracker>()?.Mark(player);

                // 11b. OnOpponentWordResolved — reactive economy/penalties on every OTHER active player
                //      (Tax Collector, Toll Booth, Bounty Hunter), each routed through their cards.
                foreach (var other in players)
                {
                    if (other.UserId == player.UserId || other.IsEliminated || other.HasLeft)
                        continue;
                    int oidx = players.IndexOf(other);
                    var octx = new EngineEvaluationContext(word, Array.Empty<char>(), players)
                    {
                        Bay = other.EngineBay,
                        Services = services,
                        PlayerIndex = oidx,
                        Resolution = resolution,
                    };
                    foreach (var card in other.EngineBay)
                        octx = card.OnOpponentWordResolved(octx, card);
                }

                // 11c. OnTurnEnded — the submitter's automated aggression (Flak Cannon time-shaves,
                //      Bait & Switch letter-hijacks), each routed through the victim's Titanium Mirror.
                evalCtx = evalCtx with { Resolution = resolution };
                foreach (var card in player.EngineBay)
                    evalCtx = card.OnTurnEnded(evalCtx, card);
            }

            // Gather the per-resolution outputs the cards produced.
            IReadOnlyList<string> collectors = services.EraTaxCollectors.Count > 0
                ? services.EraTaxCollectors.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
            int bounty = services.EraTaxBounty;
            var effects = services.Notices.ToList();

            // Log the accepted play for the UI feed.
            state.PlayLog.Add(new AlphaChainWordPlay(
                DateTimeOffset.UtcNow, cmd.ActorUserId, player?.DisplayName ?? cmd.ActorUserId,
                word, score, taxed, bounty));

            // Publish the scoring trace + any fired effects so every client plays the replay strip.
            state.ScoreReplaySequence++;
            state.LatestScoreReplay = new ScoreReplay(
                state.ScoreReplaySequence, cmd.ActorUserId, player?.DisplayName ?? cmd.ActorUserId,
                breakdown, bounty, collectors, effects);
            if (effects.Count > 0)
            {
                state.LatestEngineNotices = effects;
                state.EngineNoticeSequence++;
            }

            context.LastSubmitResult = (taxed && score == 0)
                ? new SubmitWordResult.AcceptedZeroPointTax()
                : new SubmitWordResult.Accepted(score);

            return AdvanceTurnAndEvaluate(context, DateTimeOffset.UtcNow, holdForReplay: true);
        }

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
        private static void BeginTurnFor(AlphaChainGameContext context, string? userId)
        {
            if (userId is not null && context.State.GamePlayers.TryGetValue(userId, out var player))
                context.EvaluationServices.FireTurnStarted(player);
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
            state.GamePlayers.TryGetValue(current ?? string.Empty, out var player);

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

            if (state.GamePlayers.TryGetValue(current, out var player))
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

                if (state.GamePlayers.TryGetValue(id, out var ps) && !ps.IsEliminated && !ps.HasLeft)
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
