using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;
using KnockBox.Tracery.Services.Projection;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.WordService.Contracts;

namespace KnockBox.Tracery.Services.Logic.Games
{
    public class TraceryGameEngine(
        IWordListService wordListService,
        IRandomNumberService rng,
        ILogger<TraceryGameEngine> logger,
        ILogger<TraceryGameState> stateLogger)
        : AbstractGameEngine<TraceryGameState>(2, 8), IGameStateProjector, IGameCommandHandler
    {
        // Per-recipient projector + the wire-command JSON options (string enums + case-insensitive
        // so the browser's source-gen JSON parses on the server). The hub resolves both interfaces
        // off this keyed engine instance; per-room data lives on TraceryGameState, so the projector
        // is stateless and shared. Min/max mirror the base ctor's (2, 8).
        private readonly TraceryStateProjector _projector = new(2, 8);

        private static readonly JsonSerializerOptions CommandJsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
        };

        // ═══════════════════════════════════════════════════════════════════════
        // Hub surface (WASM tri-split): per-recipient projection + command dispatch.
        // The Razor UI moved to KnockBox.Tracery.Client; these adapt the existing
        // engine methods to the (command, payloadJson) the hub delivers. Lobby
        // creation is NOT a command — it flows through GameHub.CreateRoom →
        // CreateStateAsync. The FSM still auto-advances on the engine's own
        // ScheduleCallback timers (no IServerTickHandler needed), and every
        // ExecuteAsync re-projects via the GameViewCoordinator subscription.
        // ═══════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public object? ProjectFor(AbstractGameState state, Guid recipientId)
            => ((IGameStateProjector)_projector).ProjectFor(state, recipientId);

        /// <inheritdoc />
        public async ValueTask<Result> HandleCommandAsync(
            User caller, AbstractGameState state, string command, string? payloadJson, CancellationToken ct = default)
        {
            if (state is not TraceryGameState s)
                return Result.FromError("Invalid game state for Tracery.");

            return command switch
            {
                TraceryCommands.Start          => await StartFromPayload(caller, s, payloadJson, ct),
                TraceryCommands.SubmitTrace    => SubmitTraceFromPayload(caller, s, payloadJson),
                TraceryCommands.SkipReveal     => SkipReveal(s, caller),
                TraceryCommands.UpdateSettings => UpdateSettingsFromPayload(caller, s, payloadJson),
                TraceryCommands.KickPlayer     => KickFromPayload(caller, s, payloadJson),
                TraceryCommands.ReturnToLobby  => ReturnToLobby(caller, s),
                _ => Result.FromError($"Unknown command [{command}].")
            };
        }

        // Host participation is a start-time choice (mirrors the old "Start as Player" button); a
        // non-host caller can't change it because StartAsync rejects a non-host start anyway.
        private async Task<Result> StartFromPayload(User caller, TraceryGameState state, string? payloadJson, CancellationToken ct)
        {
            bool hostPlays = Deserialize<StartPayload>(payloadJson)?.HostPlays ?? false;
            if (caller.Id == state.Host.Id
                && state.UpdateSettings(cfg => cfg with { HostPlaysAlong = hostPlays }).TryGetFailure(out var settingsError))
                return settingsError;
            return await StartAsync(caller, state, ct);
        }

        private Result SubmitTraceFromPayload(User caller, TraceryGameState state, string? payloadJson)
        {
            if (Deserialize<SubmitTracePayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed submit-trace payload.");
            return SubmitTrace(state, caller, p.Path ?? []);
        }

        private Result UpdateSettingsFromPayload(User caller, TraceryGameState state, string? payloadJson)
        {
            // Compare by id, never by reference: the hub resolves a fresh User per command.
            if (caller.Id != state.Host.Id)
                return Result.FromError("Only the host can change the settings.");
            if (Deserialize<TracerySettingsView>(payloadJson) is not { } view)
                return Result.FromError("Malformed settings payload.");
            return state.UpdateSettings(cfg => cfg.Apply(view));
        }

        private Result KickFromPayload(User caller, TraceryGameState state, string? payloadJson)
        {
            if (Deserialize<KickPlayerPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed kick payload.");
            User? target = null;
            foreach (var entry in state.RosterIncludingHost)
                if (entry.User.Id == p.PlayerId) { target = entry.User; break; }
            if (target is null)
                return Result.FromError("That player is not in the lobby.");
            return state.KickPlayer(caller, target);
        }

        private static T? Deserialize<T>(string? json)
            => string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json, CommandJsonOptions);

        // The trie is shared across every lobby this singleton serves, so it is built
        // with the smallest word length the game ever allows (the settings panel clamps
        // MinWordLength to [3, 8]). Per-round filtering by the lobby's actual minimum
        // happens in TracerySolver.Solve; building with the global floor keeps the trie
        // valid no matter what any individual lobby picks. A shorter floor would only
        // bloat the trie; a longer one would silently drop legal short words.
        internal const int MinSupportedWordLength = 3;

        // One trie per word pool, built lazily and reused for the engine's lifetime: the
        // host can pick different dictionaries for board generation and answer validation,
        // so up to a handful of tries may be cached (Full ~10 MB, Reduced small, NYT tiny).
        // Lazy<T> values guarantee each pool's heavy build runs at most once even when two
        // first lobbies race — the same guarantee the old single-trie LazyInitializer gave.
        private readonly ConcurrentDictionary<WordPoolMode, Lazy<TraceryTrie>> _tries = new();

        /// <summary>
        /// Returns a solver bound to the trie for <paramref name="mode"/>, building that trie
        /// on first use. The mode is resolved first (an empty pool — e.g. the curated common
        /// list before its CSV ships — falls back to the full dictionary). Thread-safe.
        /// </summary>
        internal TracerySolver GetSolver(WordPoolMode mode)
            => new(GetTrie(ResolveMode(mode)));

        /// <summary>
        /// Returns a board generator that judges/seeds candidates against
        /// <paramref name="generationMode"/>. Cheap to construct (holds only references), so
        /// it is built per call rather than cached — the heavy trie behind it is cached.
        /// </summary>
        internal GridGenerator GetGenerator(WordPoolMode generationMode)
        {
            var effective = ResolveMode(generationMode);
            return new GridGenerator(new TracerySolver(GetTrie(effective)), rng, wordListService, logger, effective);
        }

        // Resolves a requested pool to one that actually has words. Reduced is empty until its
        // CSV ships, and unknown/unbacked modes have no pool — all fall back to the full
        // dictionary so a board is always generatable and answers always validatable.
        private WordPoolMode ResolveMode(WordPoolMode requested)
        {
            if (requested != WordPoolMode.FullDictionary && !wordListService.GetAvailableLengths(requested).Any())
            {
                logger.LogWarning(
                    "Tracery dictionary pool {Mode} is empty; falling back to FullDictionary.", requested);
                return WordPoolMode.FullDictionary;
            }
            return requested;
        }

        private TraceryTrie GetTrie(WordPoolMode mode)
            => _tries.GetOrAdd(mode, m => new Lazy<TraceryTrie>(() => BuildTrie(m))).Value;

        private TraceryTrie BuildTrie(WordPoolMode mode)
        {
            logger.LogInformation(
                "Building Tracery dictionary trie for {Mode} (min word length {min}).", mode, MinSupportedWordLength);
            var trie = TraceryTrie.BuildFrom(wordListService, MinSupportedWordLength, mode);
            logger.LogInformation("Tracery dictionary trie built for {Mode}.", mode);
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

        // Fires outside the execute lock so the handler may safely call Execute. In Standard mode a
        // mid-round leaver simply stops banking and the timer still ends the round — nothing to do.
        // In Search mode the round ends early once every participant has completed the list, so a
        // disconnect (which shrinks the roster) can newly satisfy that gate: re-check it here and
        // close the round if the remaining players have all finished.
        private void HandlePlayerLeft(TraceryGameState s, User player)
        {
            if (s.Settings.Mode != GameMode.Search) return;

            s.Execute(() =>
            {
                if (s.Phase == GamePhase.Playing && s.IsRoundActive && AllParticipantsCompleted(s))
                    CompleteRound(s);
            });
        }

        /// <summary>
        /// Hooks for the base <see cref="AbstractGameEngine{TState}.ReturnToLobby"/> (host-only,
        /// terminal-phase-only). Resetting to <see cref="GamePhase.Lobby"/> and re-enabling joins
        /// re-renders every player's page at the lobby — no navigation needed.
        /// </summary>
        protected override bool IsTerminalPhase(TraceryGameState state) => state.Phase == GamePhase.FinalStandings;

        /// <inheritdoc />
        protected override void ResetForLobby(TraceryGameState state)
        {
            state.ResetForLobby();
            state.Phase = GamePhase.Lobby;
        }

        protected override Task<Result> StartAsyncCore(TraceryGameState s, CancellationToken ct = default)
        {
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

        // Picks up to `size` distinct words at random from the candidate set (the board's
        // findable words). A partial Fisher-Yates over a snapshot keeps each draw uniform and
        // distinct; the count is clamped to what the board offers so a sparse board can't ask for
        // more words than exist. Returns lower-cased words (the solver already lower-cases keys).
        private ImmutableArray<string> BuildSearchList(IEnumerable<string> candidates, int size)
        {
            var pool = candidates.ToArray();
            int take = Math.Min(Math.Max(size, 0), pool.Length);
            if (take == 0) return [];

            for (int i = 0; i < take; i++)
            {
                int j = i + rng.GetRandomInt(pool.Length - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return [.. pool.Take(take)];
        }

        // Search mode early-end gate: true once every current participant has found the whole list.
        // Reads the live roster, so a participant who disconnects mid-round no longer blocks the
        // gate (their re-check happens in HandlePlayerLeft). A missing/empty list never gates here
        // because no word can be banked against it, so no completion is ever stamped.
        private static bool AllParticipantsCompleted(TraceryGameState s)
        {
            if (s.SearchList.Length == 0) return false;
            bool any = false;
            foreach (var entry in Roster(s))
            {
                any = true;
                if (!s.PlayerStates.TryGetValue(entry.User.Id, out var ps) || ps.CompletionRank is null)
                    return false;
            }
            return any;
        }

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
                //    Validate against the answer dictionary so a player can bank any word it
                //    accepts — even obscure ones absent from the board (generation) dictionary.
                var validation = GetSolver(state.Settings.ValidationDictionary)
                    .ValidateTrace(state.CurrentGrid, path, state.Settings.MinWordLength);
                if (!validation.TryGetSuccess(out var word))
                {
                    validation.TryGetFailure(out var valErr);
                    return Result.FromError(valErr);
                }

                // 4b. Search mode: only words on the shared list score. A valid word that isn't a
                //     target is ignored (soft reject) — it's neither banked nor counted.
                if (state.Settings.Mode == GameMode.Search && !state.IsSearchTarget(word))
                    return Result.FromError("Not on the search list.");

                // 5. Already banked this round → no-op success (scores once per player per round).
                if (pState.HasBanked(word))
                    return Result.Success;

                // 6. Bank it. The path is copied so a later client reuse of its buffer can't
                //    mutate the stored trace. (Point value is computed at round close in M06.)
                pState.Bank(new TracedWord(word, path.ToArray()));

                // 7. Search mode: a player who has now found every target completes the list. Stamp
                //    their finishing place, and if everyone has finished, end the round early rather
                //    than waiting out the clock.
                if (state.Settings.Mode == GameMode.Search
                    && pState.CompletionRank is null
                    && pState.BankedWords.Count >= state.SearchList.Length)
                {
                    pState.CompletionRank = ++state.SearchCompletionsThisRound;
                    if (AllParticipantsCompleted(state))
                        CompleteRound(state);
                }
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

            // Generate the next board on a worker thread *during* the intro countdown rather than
            // inside the round-start lock. Generation runs up to DefaultMaxAttempts full DFS solves;
            // doing it under the state's exclusive write lock would block every SubmitTrace and
            // SkipReveal for its whole duration. Settings is an immutable record, so snapshotting the
            // reference here (inside the lock) and reading it off-thread is safe. The intro window
            // almost always outlasts generation, so when the scheduled callback fires the board is
            // already in hand and the lock is held only for the fast commit in EnterPlaying.
            var settings = s.Settings;
            var generation = Task.Run(() => GenerateRoundBoard(settings));

            s.ScheduleCallback(duration, async () =>
            {
                var board = await generation.ConfigureAwait(false);
                EnterPlaying(s, board);
            });
        }

        /// <summary>
        /// Builds a round's board off the execute lock (see <see cref="EnterRoundIntro"/>): generate
        /// against the board (generation) dictionary — the solve the generator already computed while
        /// clearing the quality bar is the board's common-word set — then derive the validation set
        /// (everything bankable), re-solving the accepted grid against the answer dictionary only when
        /// the two dictionaries differ. Pure: touches no game state. A generation failure is carried
        /// back as <see cref="RoundBoard.Error"/> so the committing <see cref="EnterPlaying"/> can log
        /// it against the correct round number.
        /// </summary>
        internal RoundBoard GenerateRoundBoard(TracerySettings settings)
        {
            var gen = GetGenerator(settings.GenerationDictionary).Generate(settings);
            if (gen.TryGetSuccess(out var board))
            {
                var genMode = ResolveMode(settings.GenerationDictionary);
                var valMode = ResolveMode(settings.ValidationDictionary);
                var findable = valMode == genMode
                    ? board.FindableWords // same dictionary → reuse, no second solve
                    : GetSolver(valMode).Solve(board.Grid, settings.MinWordLength);
                return new RoundBoard(board.Grid, board.FindableWords, findable, null);
            }

            gen.TryGetFailure(out var err);
            return new RoundBoard(null,
                ImmutableDictionary<string, TracedWord>.Empty,
                ImmutableDictionary<string, TracedWord>.Empty,
                err.InternalMessage);
        }

        /// <summary>
        /// Convenience for the synchronous start path and unit tests: generate inline, then commit.
        /// The production round-start path pre-generates off the lock (<see cref="EnterRoundIntro"/>)
        /// and calls the <see cref="EnterPlaying(TraceryGameState, RoundBoard)"/> overload directly.
        /// </summary>
        internal void EnterPlaying(TraceryGameState s) => EnterPlaying(s, GenerateRoundBoard(s.Settings));

        /// <summary>
        /// Commits a (pre-)generated board and opens the round. Assumes it is already inside the
        /// execute lock; kept deliberately fast (no generation here) so the lock is held only briefly.
        /// </summary>
        internal void EnterPlaying(TraceryGameState s, RoundBoard board)
        {
            s.CurrentRound++;
            foreach (var entry in Roster(s))
                s.CreatePlayerState(entry.User.Id).ResetRound();

            if (board.Grid is not null)
            {
                s.CurrentGrid = board.Grid;
                s.BoardFindableWords = board.BoardFindableWords;
                s.FindableWords = board.FindableWords;
            }
            else
            {
                // Settings clamp MinWordLength <= cell count and the generator has a seed
                // fallback, so this is effectively unreachable in production. Fail safe rather
                // than crash the scheduled callback: log, leave an empty board, and let the
                // round time out normally.
                logger.LogError("Tracery board generation failed for round {Round}: {Error}", s.CurrentRound, board.Error);
                s.CurrentGrid = null;
                s.FindableWords = ImmutableDictionary<string, TracedWord>.Empty;
                s.BoardFindableWords = ImmutableDictionary<string, TracedWord>.Empty;
            }

            // Search mode: draw the round's shared target list from the board's common-word set so
            // every word is recognizable and guaranteed present on the grid. Clamped to what the
            // board actually offers. Standard mode leaves the list empty.
            s.SearchCompletionsThisRound = 0;
            s.SearchList = s.Settings.Mode == GameMode.Search
                ? BuildSearchList(s.BoardFindableWords.Keys, s.Settings.SearchListSize)
                : [];

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

            // Scoring (GDD §5, §9): in Standard mode unique-find can only be resolved once the round
            // is locked and every bank is final, so scoring happens here rather than at submit time.
            // Search mode scores flat per found word plus a placement bonus by finishing order.
            var roster = Roster(s).ToList();

            var outcomes = s.Settings.Mode == GameMode.Search
                ? ScoreSearchRound(s, roster)
                : ScoreStandardRound(s, roster);

            // Persist the round so the reveal/standings screens render from it directly.
            var result = new RoundResult
            {
                RoundNumber = s.CurrentRound,
                Outcomes = outcomes
            };
            s.RoundResults = s.RoundResults.Add(result);

            // Assemble the reveal. Standard mode builds the GDD §7 beats from the round's two
            // findable sets (validation set drives the theoretical max + beat paths so scores never
            // exceed it; board set drives "words nobody found"). Search mode has its own reveal view
            // that reads the round outcomes directly, so there is nothing to pre-assemble.
            s.CurrentReveal = s.Settings.Mode == GameMode.Search
                ? null
                : RevealBuilder.Build(s.FindableWords, s.BoardFindableWords, result, s.Settings);

            EnterReveal(s);
        }

        // Standard scoring: base + length + rare-letter per word, with the unique-find multiplier
        // resolved across all banks once the round is locked.
        private static ImmutableArray<TraceryPlayerRoundOutcome> ScoreStandardRound(
            TraceryGameState s, IReadOnlyList<PlayerEntry> roster)
        {
            // Global frequency: how many players banked each word this round. A word with count 1 is
            // a unique find; count >= 2 earns no multiplier for anyone.
            var bankedByCount = new Dictionary<string, int>();
            foreach (var entry in roster)
                if (s.PlayerStates.TryGetValue(entry.User.Id, out var ps))
                    foreach (var word in ps.BankedWords.Keys)
                        bankedByCount[word] = bankedByCount.GetValueOrDefault(word) + 1;

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

            return outcomes.ToImmutable();
        }

        // Search scoring: each found list-word is worth a flat amount (its length — no length, rare,
        // or unique bonuses, since everyone shares the same list). Players who found every word earn
        // a placement bonus by finishing order that scales with the player count: the first finisher
        // gets unit × P, the next unit × (P-1), and so on; non-completers get no bonus.
        private static ImmutableArray<TraceryPlayerRoundOutcome> ScoreSearchRound(
            TraceryGameState s, IReadOnlyList<PlayerEntry> roster)
        {
            int participantCount = roster.Count;
            int unit = s.Settings.SearchPlacementBonusUnit;
            int listSize = s.SearchList.Length;

            var outcomes = ImmutableArray.CreateBuilder<TraceryPlayerRoundOutcome>(roster.Count);
            foreach (var entry in roster)
            {
                if (!s.PlayerStates.TryGetValue(entry.User.Id, out var ps))
                    continue;

                var wordScores = ImmutableArray.CreateBuilder<TraceryWordScore>(ps.BankedWords.Count);
                int wordPoints = 0;
                foreach (var word in ps.BankedWords.Keys)
                {
                    int points = TraceryScorer.BaseScore(word);
                    wordScores.Add(new TraceryWordScore { Word = word, BaseScore = points, Points = points });
                    wordPoints += points;
                }

                // Placement bonus only for players who completed the whole list.
                int bonus = ps.CompletionRank is int rank
                    ? Math.Max(0, unit * (participantCount - rank + 1))
                    : 0;
                ps.CompletionBonus = bonus;

                int roundScore = wordPoints + bonus;
                ps.RoundScore = roundScore;
                ps.LastRoundPoints = roundScore;
                ps.CumulativeScore += roundScore;

                outcomes.Add(new TraceryPlayerRoundOutcome
                {
                    UserId = entry.User.Id,
                    DisplayName = entry.DisplayName,
                    PointsAwarded = roundScore,
                    CumulativeScore = ps.CumulativeScore,
                    WordScores = wordScores.ToImmutable(),
                    CompletionRank = ps.CompletionRank,
                    CompletionBonus = bonus,
                    WordsFound = ps.BankedWords.Count,
                    SearchListSize = listSize
                });
            }

            return outcomes.ToImmutable();
        }

        // The single post-round intermission: the reveal shows words found, round scoring, the
        // cumulative-score standings, and a next-round indicator, then auto-advances straight into
        // the next round (or final standings) — no separate round-over or between-round intro hops.
        internal void EnterReveal(TraceryGameState s)
        {
            var duration = s.Settings.IntermissionDuration;
            s.Phase = GamePhase.Reveal;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            int capturedRound = s.CurrentRound;
            s.ScheduleCallback(duration, () =>
            {
                AdvanceAfterResultsIfStillRevealing(s, capturedRound);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Host-only: ends the post-round reveal early without waiting out the intermission timer.
        /// Rather than jumping straight into the next round, it hands off to the round-intro
        /// transition view (<see cref="EnterRoundIntro"/>) — or, on the final round, to final
        /// standings. The still-pending intermission callback is rendered inert by the round/phase
        /// guard in <see cref="AdvanceAfterResultsIfStillRevealing"/>, so there is no double-advance.
        /// </summary>
        public Result SkipReveal(TraceryGameState state, User caller)
        {
            var executeResult = state.Execute<Result>(() =>
            {
                if (caller is null || caller.Id != state.Host.Id)
                    return Result.FromError("Only the host can skip the round transition.");
                if (state.Phase != GamePhase.Reveal)
                    return Result.FromError("There is no round transition to skip.");

                EnterRoundIntro(state);
                return Result.Success;
            });

            if (executeResult.TryGetSuccess(out var inner)) return inner;
            if (executeResult.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.FromCancellation();
        }

        // Guards the scheduled intermission timer against a host who skipped early: once the host
        // advances, the captured round (or the phase) no longer matches, so the stale callback
        // no-ops instead of double-advancing. Mirrors EndRoundIfStillActive's staleness check.
        internal void AdvanceAfterResultsIfStillRevealing(TraceryGameState s, int roundNum)
        {
            if (s.Phase != GamePhase.Reveal || s.CurrentRound != roundNum) return;
            AdvanceAfterResults(s);
        }

        internal void AdvanceAfterResults(TraceryGameState s)
        {
            if (s.CurrentRound >= s.Settings.TotalRounds)
                EnterFinalStandings(s);
            else
                EnterPlaying(s);
        }

        internal void EnterFinalStandings(TraceryGameState s)
        {
            s.Phase = GamePhase.FinalStandings;
            s.IsRoundActive = false;
            s.PhaseExpiresAtUtc = null;
        }
    }

    /// <summary>
    /// A round's generated board plus its two findable-word sets, produced off the execute lock by
    /// <see cref="TraceryGameEngine.GenerateRoundBoard"/> and committed under the lock by
    /// <see cref="TraceryGameEngine.EnterPlaying(TraceryGameState, RoundBoard)"/>. <see cref="Grid"/>
    /// is null on a generation failure, in which case <see cref="Error"/> carries the reason.
    /// </summary>
    internal readonly record struct RoundBoard(
        Grid? Grid,
        IReadOnlyDictionary<string, TracedWord> BoardFindableWords,
        IReadOnlyDictionary<string, TracedWord> FindableWords,
        string? Error);
}
