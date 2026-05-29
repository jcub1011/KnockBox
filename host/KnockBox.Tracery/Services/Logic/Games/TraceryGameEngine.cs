using System.Collections.Immutable;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.WordService.Contracts;

namespace KnockBox.Tracery.Services.Logic.Games
{
    public class TraceryGameEngine(
        IWordListService wordListService,
        IRandomNumberService rng,
        ILogger<TraceryGameEngine> logger,
        ILogger<TraceryGameState> stateLogger) : AbstractGameEngine(2, 8)
    {
        // The trie is shared across every lobby this singleton serves, so it is built
        // with the smallest word length the game ever allows (the settings panel clamps
        // MinWordLength to [3, 8]). Per-round filtering by the lobby's actual minimum
        // happens in TracerySolver.Solve; building with the global floor keeps the trie
        // valid no matter what any individual lobby picks. A shorter floor would only
        // bloat the trie; a longer one would silently drop legal short words.
        internal const int MinSupportedWordLength = 3;

        // Lazily built once and reused: the ~386k-word load cost is paid at the first
        // lobby that needs a solver, not on every round. LazyInitializer guards against
        // two concurrent first lobbies both building it.
        private TraceryTrie? _trie;

        // The generator depends on the lazily-built solver, so build it lazily too: the
        // trie cost is still paid at most once, on the first lobby that needs a board.
        private GridGenerator? _generator;

        /// <summary>
        /// Returns a solver bound to the shared dictionary trie, building the trie on the
        /// first call. Thread-safe; the heavy build runs at most once per engine.
        /// </summary>
        internal TracerySolver GetSolver()
            => new(LazyInitializer.EnsureInitialized(ref _trie, BuildTrie));

        /// <summary>
        /// Returns the board generator, constructing it (and, transitively, the shared
        /// solver/trie) on first use. Thread-safe and reused across every lobby and round.
        /// </summary>
        internal GridGenerator GetGenerator()
            => LazyInitializer.EnsureInitialized(ref _generator,
                () => new GridGenerator(GetSolver(), rng, wordListService, logger));

        private TraceryTrie BuildTrie()
        {
            logger.LogInformation("Building Tracery dictionary trie (min word length {min}).", MinSupportedWordLength);
            var trie = TraceryTrie.BuildFrom(wordListService, MinSupportedWordLength);
            logger.LogInformation("Tracery dictionary trie built.");
            return trie;
        }

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new TraceryGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            // Disconnect hook (Spardle precedent). The token is intentionally not stored: the
            // subscription is scoped to this state's lifetime and released when it is disposed.
            gameState.SubscribePlayerUnregistered(player => HandlePlayerLeft(gameState, player));
            logger.LogInformation("Created Tracery gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        // Fires outside the execute lock so the handler may safely call Execute. Tracery players
        // bank independently against the round clock — a mid-round leaver simply stops banking
        // and the timer still ends the round, so there is no "all finished early" gate to
        // re-open here. Hook retained for parity with the platform disconnect contract; M05/M06
        // re-check round-end here if an early-finish optimization is ever added.
        private void HandlePlayerLeft(TraceryGameState s, User player)
        {
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not TraceryGameState s)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{state?.GetType().Name ?? "null"}] couldn't be cast to type [{nameof(TraceryGameState)}]."));

            var execResult = s.Execute(() =>
            {
                // SetJoinable(false) closes the join race before we read Players.Length;
                // once the lobby is non-joinable, RegisterPlayer rejects new joins.
                s.SetJoinable(false);
                s.SetHostIsParticipant(s.Players.Length == 0 || s.Settings.HostPlaysAlong);
                // Freeze the participant roster now so the final-standings screen can show
                // everyone even after disconnects prune them from the live Players roster.
                s.SetParticipants(s.HostIsParticipant ? s.RosterIncludingHost : s.Players);

                s.CurrentRound = 0;
                s.RoundResults = s.RoundResults.Clear();

                foreach (var entry in Roster(s))
                {
                    var ps = s.CreatePlayerState(entry.User.Id);
                    ps.CumulativeScore = 0;
                    ps.LastRoundPoints = 0;
                    ps.ResetRound();
                }

                EnterRoundIntro(s);
                return Result.Success;
            });

            if (execResult.TryGetSuccess(out var inner)) return Task.FromResult(inner);
            if (execResult.TryGetFailure(out var err)) return Task.FromResult(Result.FromError(err));
            return Task.FromResult(Result.FromCancellation());
        }

        // The gameplay roster — registered players, plus the host when participating.
        private static IEnumerable<PlayerEntry> Roster(TraceryGameState s)
            => s.HostIsParticipant ? s.RosterIncludingHost : s.Players;

        // ═══════════════════════════════════════════════════════════════════════
        // Player input
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Validates and banks a player's traced word. The path is an ordered list of grid
        /// cell ids captured client-side by drag or tap; the client only previews legality —
        /// this is the authoritative check. Mirrors <c>SpardleEngine.SubmitGuess</c>: all work
        /// happens inside <see cref="AbstractGameState.Execute{T}(Func{T})"/> so subscribers see
        /// one consistent transition. A word already banked this round is a silent no-op success
        /// (GDD §4: a word scores once per player per round, regardless of path). Cells are never
        /// consumed, so they remain available for other words.
        /// </summary>
        public Result SubmitTrace(TraceryGameState state, User player, IReadOnlyList<int> path)
        {
            var executeResult = state.Execute<Result>(() =>
            {
                // 1. An observing host is the shared display, not a participant.
                if (player.Id == state.Host.Id && !state.HostIsParticipant)
                    return Result.FromError("Host is observing and cannot submit traces.");

                // 2. Input gate (Milestone 04): only the live Playing phase accepts traces, so a
                //    submission that races a just-expired timer is rejected.
                if (state.Phase != GamePhase.Playing || !state.IsRoundActive)
                    return Result.FromError("Round is not active.");

                // 3. Reject strangers before materializing a PlayerState entry for them.
                if (!state.TryGetPlayerState(player.Id, out var pState))
                    return Result.FromError("You are not a participant in this round.");

                // Defensive: an active round always has a grid (EnterPlaying sets it before
                // flipping IsRoundActive), so this is effectively unreachable.
                if (state.CurrentGrid is null)
                    return Result.FromError("There is no active grid.");

                // 4. The solver is the single source of adjacency/length/dictionary truth.
                var validation = GetSolver().ValidateTrace(state.CurrentGrid, path, state.Settings.MinWordLength);
                if (!validation.TryGetSuccess(out var word))
                {
                    validation.TryGetFailure(out var valErr);
                    return Result.FromError(valErr);
                }

                // 5. Already banked this round → no-op success (scores once per player per round).
                if (pState.HasBanked(word))
                    return Result.Success;

                // 6. Bank it. The path is copied so a later client reuse of its buffer can't
                //    mutate the stored trace. (Point value is computed at round close in M06.)
                pState.Bank(new TracedWord(word, path.ToArray()));
                return Result.Success;
            });

            if (executeResult.TryGetSuccess(out var inner)) return inner;
            if (executeResult.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.FromCancellation();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Phase transitions — placeholder flow for Milestone 01. Real grid,
        // tracing, and scoring fill in later milestones. Every helper assumes it
        // is already inside the execute lock (either via Execute/ExecuteAsync
        // directly, or via ScheduleCallback which wraps its action in ExecuteAsync).
        // They are internal so unit tests can drive transitions without waiting on
        // wall-clock timers.
        // ═══════════════════════════════════════════════════════════════════════

        internal void EnterRoundIntro(TraceryGameState s)
        {
            if (s.CurrentRound >= s.Settings.TotalRounds)
            {
                EnterFinalStandings(s);
                return;
            }

            var duration = s.Settings.TransitionDuration;
            s.Phase = GamePhase.RoundIntro;
            s.IsRoundActive = false;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            s.ScheduleCallback(duration, () =>
            {
                EnterPlaying(s);
                return Task.CompletedTask;
            });
        }

        internal void EnterPlaying(TraceryGameState s)
        {
            s.CurrentRound++;
            foreach (var entry in Roster(s))
                s.CreatePlayerState(entry.User.Id).ResetRound();

            // Generate the round's board and reuse the solve the generator already computed
            // while clearing the quality bar — never re-solve the same grid.
            var gen = GetGenerator().Generate(s.Settings);
            if (gen.TryGetSuccess(out var board))
            {
                s.CurrentGrid = board.Grid;
                s.FindableWords = board.FindableWords;
            }
            else
            {
                // Settings clamp MinWordLength <= cell count and the generator has a seed
                // fallback, so this is effectively unreachable in production. Fail safe rather
                // than crash the scheduled callback: log, leave an empty board, and let the
                // round time out normally.
                gen.TryGetFailure(out var err);
                logger.LogError("Tracery board generation failed for round {Round}: {Error}", s.CurrentRound, err.InternalMessage);
                s.CurrentGrid = null;
                s.FindableWords = ImmutableDictionary<string, TracedWord>.Empty;
            }

            s.RoundStartTime = DateTimeOffset.UtcNow;
            s.IsRoundActive = true;
            s.Phase = GamePhase.Playing;

            // TimeSpan.Zero = unlimited: no auto-advance — host-advanced (full host-advance UI
            // is a later milestone). Otherwise the timer ends the round on expiry.
            if (s.Settings.RoundTimer > TimeSpan.Zero)
            {
                s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + s.Settings.RoundTimer;
                int capturedRound = s.CurrentRound;
                s.ScheduleCallback(s.Settings.RoundTimer, () =>
                {
                    EndRoundIfStillActive(s, capturedRound);
                    return Task.CompletedTask;
                });
            }
            else
            {
                s.PhaseExpiresAtUtc = null;
            }
        }

        internal void EndRoundIfStillActive(TraceryGameState s, int roundNum)
        {
            // A timer captured for a prior round must not end a later one (a stale callback
            // firing after a manual/early advance). The round number is the discriminator.
            if (s.Phase != GamePhase.Playing || s.CurrentRound != roundNum) return;
            CompleteRound(s);
        }

        internal void CompleteRound(TraceryGameState s)
        {
            // Close the input gate before any reveal/scoring work — late traces are rejected
            // from here on.
            s.IsRoundActive = false;

            // Scoring (GDD §5, §9): unique-find can only be resolved once the round is locked and
            // every bank is final, so it happens here rather than at submit time.
            var roster = Roster(s).ToList();

            // 1. Global frequency: how many players banked each word this round. A word with
            //    count 1 is a unique find; count >= 2 earns no multiplier for anyone.
            var bankedByCount = new Dictionary<string, int>();
            foreach (var entry in roster)
                if (s.PlayerStates.TryGetValue(entry.User.Id, out var ps))
                    foreach (var word in ps.BankedWords.Keys)
                        bankedByCount[word] = bankedByCount.GetValueOrDefault(word) + 1;

            // 2. Score each player's banks, building the per-word breakdown the reveal reads.
            var outcomes = ImmutableArray.CreateBuilder<TraceryPlayerRoundOutcome>(roster.Count);
            foreach (var entry in roster)
            {
                if (!s.PlayerStates.TryGetValue(entry.User.Id, out var ps))
                    continue;

                var wordScores = ImmutableArray.CreateBuilder<TraceryWordScore>(ps.BankedWords.Count);
                int roundScore = 0;
                foreach (var word in ps.BankedWords.Keys)
                {
                    bool isUnique = bankedByCount.GetValueOrDefault(word) == 1;
                    var breakdown = TraceryScorer.Score(word, isUnique, s.Settings);
                    wordScores.Add(breakdown);
                    roundScore += breakdown.Points;
                }

                // 3. Roll the round into the cumulative total.
                ps.RoundScore = roundScore;
                ps.LastRoundPoints = roundScore;
                ps.CumulativeScore += roundScore;

                outcomes.Add(new TraceryPlayerRoundOutcome
                {
                    UserId = entry.User.Id,
                    DisplayName = entry.DisplayName,
                    PointsAwarded = roundScore,
                    CumulativeScore = ps.CumulativeScore,
                    WordScores = wordScores.ToImmutable()
                });
            }

            // 4. Persist the round so the reveal/standings screens render from it directly.
            var result = new RoundResult
            {
                RoundNumber = s.CurrentRound,
                Outcomes = outcomes.ToImmutable()
            };
            s.RoundResults = s.RoundResults.Add(result);

            // 5. Assemble the host reveal (GDD §7) from the solver's findable set + the scored
            //    round. Pure projection — no recompute — stored for the Reveal-phase view.
            s.CurrentReveal = RevealBuilder.Build(s.FindableWords, result, s.Settings);

            EnterReveal(s);
        }

        internal void EnterReveal(TraceryGameState s)
        {
            var duration = s.Settings.TransitionDuration;
            s.Phase = GamePhase.Reveal;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            s.ScheduleCallback(duration, () =>
            {
                EnterRoundOver(s);
                return Task.CompletedTask;
            });
        }

        internal void EnterRoundOver(TraceryGameState s)
        {
            var duration = s.Settings.TransitionDuration;
            s.Phase = GamePhase.RoundOver;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            s.ScheduleCallback(duration, () =>
            {
                AdvanceAfterResults(s);
                return Task.CompletedTask;
            });
        }

        internal void AdvanceAfterResults(TraceryGameState s)
        {
            if (s.CurrentRound >= s.Settings.TotalRounds)
                EnterFinalStandings(s);
            else
                EnterRoundIntro(s);
        }

        internal void EnterFinalStandings(TraceryGameState s)
        {
            s.Phase = GamePhase.FinalStandings;
            s.IsRoundActive = false;
            s.PhaseExpiresAtUtc = null;
        }
    }
}
