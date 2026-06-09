using System.Text.Json;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Pages.Bench;
using KnockBox.AlphaChain.Services.Projection;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.WordService.Contracts;

namespace KnockBox.AlphaChain.Services.Logic.Games
{
    /// <summary>
    /// Server-authoritative, FSM-driven engine for Alpha Chain. The engine is a
    /// singleton; all mutable per-room state lives on <see cref="AlphaChainGameState"/>
    /// (and its <see cref="AlphaChainGameContext"/>).
    /// </summary>
    /// <remarks>
    /// <paramref name="wordList"/> (dictionary validation) and <paramref name="rng"/> are
    /// injected from DI and forwarded onto <see cref="AlphaChainGameContext"/> so the FSM
    /// states can validate words and draw the banned letter deterministically in tests.
    /// <c>IWordListService</c> is registered by the <c>KnockBox.WordService</c> library
    /// plugin, which the host loads before any game plugin — no host wiring is needed here.
    /// </remarks>
    public class AlphaChainGameEngine(
        IWordListService wordList,
        IRandomNumberService rng,
        IEngineEvaluator evaluator,
        IModifierCardFactory modifierFactory,
        ILogger<AlphaChainGameEngine> logger,
        ILogger<AlphaChainGameState> stateLogger)
        : AbstractGameEngine<AlphaChainGameState>(2, 8),
          IGameStateProjector, IGameCommandHandler, IServerTickHandler
    {
        // The projector resolves the per-card rules text (for the game-over history tooltip) from the
        // same modifier-card factory the engine uses; cards are flattened to wire DTOs server-side.
        private readonly AlphaChainStateProjector _projector = new(modifierFactory);

        // Match the hub's wire format: enums as strings, case-insensitive property names,
        // so a client-serialized command payload deserializes here.
        private static readonly JsonSerializerOptions CommandJsonOptions = new()
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
        };

        // ── Hub projection / command / tick surface ──────────────────────────

        /// <summary>Per-recipient projection entry point used by the host's <c>GameViewCoordinator</c>.</summary>
        public object? ProjectFor(AbstractGameState state, Guid recipientId)
            => ((IGameStateProjector)_projector).ProjectFor(state, recipientId);

        /// <summary>
        /// Maps a hub command name to the same engine method a Razor page used to call directly.
        /// Per-command authorization (host-only, active-player) lives in the invoked methods / FSM
        /// states. Compares the caller by <c>User.Id</c>, never by reference: the hub resolves a
        /// fresh <c>User</c> per command, so a reference check would always reject the real host.
        /// </summary>
        public async ValueTask<Result> HandleCommandAsync(
            User caller, AbstractGameState state, string command, string? payloadJson, CancellationToken ct = default)
        {
            if (state is not AlphaChainGameState s)
                return Result.FromError("Invalid game state for Alpha Chain.");

            return command switch
            {
                AlphaChainCommands.Start              => await StartFromPayload(caller, s, payloadJson, ct),
                AlphaChainCommands.SubmitWord         => await SubmitWordFromPayload(caller, s, payloadJson),
                AlphaChainCommands.AdvanceTurn        => await AdvanceTurnAsync(caller.Id, s),
                AlphaChainCommands.SubmitOptimization => await SubmitOptimizationFromPayload(caller, s, payloadJson),
                AlphaChainCommands.SelectSniperBan    => await SelectSniperBanFromPayload(caller, s, payloadJson),
                AlphaChainCommands.SkipTutorial       => await SkipTutorialAsync(caller.Id, s),
                AlphaChainCommands.ReturnToLobby      => ReturnToLobby(caller, s),
                AlphaChainCommands.UpdateSettings     => UpdateSettingsFromPayload(caller, s, payloadJson),
                AlphaChainCommands.KickPlayer         => KickFromPayload(caller, s, payloadJson),
                // ── Testing Bay (host-only dev card bench) ──
                AlphaChainCommands.BenchEnter         => await BenchEnter(caller, s),
                AlphaChainCommands.BenchExit          => BenchExit(caller, s),
                AlphaChainCommands.BenchReset         => await BenchResetFromPayload(caller, s, payloadJson),
                AlphaChainCommands.BenchSetBan        => BenchSetBanFromPayload(caller, s, payloadJson),
                AlphaChainCommands.BenchSetBay        => BenchSetBayFromPayload(caller, s, payloadJson),
                AlphaChainCommands.BenchSetScore      => BenchSetScoreFromPayload(caller, s, payloadJson),
                AlphaChainCommands.BenchSubmit        => await BenchSubmitFromPayload(caller, s, payloadJson),
                AlphaChainCommands.BenchSkip          => await BenchSkip(caller, s),
                _ => Result.FromError($"Unknown command [{command}].")
            };
        }

        /// <summary>Server-owned clock entry point; drives the FSM's time-based transitions
        /// (replacing the old host-circuit tick).</summary>
        void IServerTickHandler.Tick(AbstractGameState state, DateTimeOffset now)
        {
            if (state is AlphaChainGameState s && s.Context is not null)
                Tick(s.Context, now);
        }

        // ── Command payload adapters ─────────────────────────────────────────

        private async Task<Result> StartFromPayload(User caller, AlphaChainGameState state, string? payloadJson, CancellationToken ct)
        {
            // The two start buttons choose whether the host plays; carry it into settings before
            // the host-checked StartAsync runs StartAsyncCore (mirrors the old lobby's StartGame).
            var payload = Deserialize<StartPayload>(payloadJson);
            if (caller.Id == state.Host.Id
                && state.UpdateSettings(cfg => cfg with { HostPlays = payload?.HostPlays ?? false }).TryGetFailure(out var settingsError))
                return settingsError;
            return await StartAsync(caller, state, ct);
        }

        private async Task<Result> SubmitWordFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (Deserialize<SubmitWordPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed submit-word payload.");

            var outcome = await SubmitWordAsync(caller.Id, p.Word, state);
            if (outcome.TryGetFailure(out var error)) return error;
            if (!outcome.TryGetSuccess(out var result)) return Result.Success;

            // The typed-return pattern doesn't fit a projection-only hub: surface a rejection as a
            // Result error (the client reads SubmitCommandAsync's return value for inline feedback).
            // Accepted plays (scored or taxed) are a success — the score-replay strip shows the rest.
            return result switch
            {
                SubmitWordResult.Accepted or SubmitWordResult.AcceptedZeroPointTax => Result.Success,
                SubmitWordResult.RejectedNotYourTurn => Result.FromError("It's not your turn."),
                SubmitWordResult.RejectedChainBroken c => Result.FromError($"Word must start with '{char.ToUpperInvariant(c.Required)}'."),
                SubmitWordResult.RejectedNotInDictionary => Result.FromError("Not a word in the dictionary."),
                SubmitWordResult.RejectedDuplicate => Result.FromError("That word has already been played."),
                SubmitWordResult.RejectedEmpty => Result.FromError("Enter a word."),
                _ => Result.Success
            };
        }

        private async Task<Result> SubmitOptimizationFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (Deserialize<OptimizationPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed optimization payload.");
            return await SubmitOptimizationAsync(caller.Id, p.CardIds, state);
        }

        private async Task<Result> SelectSniperBanFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (Deserialize<SniperBanPayload>(payloadJson) is not { Letter.Length: > 0 } p)
                return Result.FromError("Malformed sniper-ban payload.");
            return await SelectSniperBanAsync(caller.Id, p.Letter[0], state);
        }

        private Result UpdateSettingsFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            // Host-only, and only meaningful before the game starts (the panel is a lobby control).
            // HostPlays is owned by the start buttons, so it is preserved across a settings edit.
            if (caller.Id != state.Host.Id)
                return Result.FromError("Only the host can change the settings.");
            if (!state.IsJoinable)
                return Result.FromError("Settings can only change before the game starts.");
            if (Deserialize<AlphaChainSettings>(payloadJson) is not { } incoming)
                return Result.FromError("Malformed settings payload.");
            return state.UpdateSettings(cur => incoming with { HostPlays = cur.HostPlays });
        }

        private Result KickFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (Deserialize<TargetPayload>(payloadJson) is not { } p)
                return Result.FromError("Malformed kick payload.");

            var target = state.Players.FirstOrDefault(e => e.User.Id == p.PlayerId).User;
            if (target is null)
                return Result.FromError("Player is not in this lobby.");
            return state.KickPlayer(caller, target);
        }

        private static T? Deserialize<T>(string? payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return default;
            try { return JsonSerializer.Deserialize<T>(payloadJson, CommandJsonOptions); }
            catch (JsonException) { return default; }
        }

        // ── Testing Bay (host-only developer card bench) ─────────────────────
        //
        // The bench is a throwaway, god-mode AlphaChainBenchScenario (its own inner engine over a
        // permissive word list + synthetic players) hung off the lobby state. Bench commands mutate
        // the scenario's INNER state, then ping the LOBBY state (RaiseBenchChanged / SetJoinable) so
        // the coordinator re-projects — the two are sequential top-level calls, never nested, to keep
        // the per-state execute-lock identity clean.

        private async Task<Result> BenchEnter(User caller, AlphaChainGameState state)
        {
            if (caller.Id != state.Host.Id) return Result.FromError("Only the host can open the Testing Bay.");
            if (state.Bench is not null) return Result.Success;
            // Single-occupant only: the lobby roster holds non-host joiners, so this must be empty.
            if (state.Players.Length != 0)
                return Result.FromError("Close the lobby to others before opening the Testing Bay.");

            var bench = new AlphaChainBenchScenario(rng, evaluator, modifierFactory, logger, stateLogger);
            await bench.ResetAsync(AlphaChainBenchScenario.MinPlayers);
            state.Bench = bench;
            // Close the lobby so no one can join while the god-mode bench is active; the Execute also
            // fans out the IsBench=true projection.
            return state.Execute(() => state.SetJoinable(false));
        }

        private Result BenchExit(User caller, AlphaChainGameState state)
        {
            if (caller.Id != state.Host.Id) return Result.FromError("Only the host can close the Testing Bay.");
            state.Bench?.Dispose();
            state.Bench = null;
            return state.Execute(() => state.SetJoinable(true));
        }

        private async Task<Result> BenchResetFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (!TryBench(caller, state, out var bench, out var err)) return err;
            if (Deserialize<BenchResetPayload>(payloadJson) is not { } p) return Result.FromError("Malformed bench-reset payload.");
            await bench.ResetAsync(p.PlayerCount);
            state.RaiseBenchChanged();
            return Result.Success;
        }

        private Result BenchSetBanFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (!TryBench(caller, state, out var bench, out var err)) return err;
            var letter = Deserialize<BenchBanPayload>(payloadJson)?.Letter;
            bench.SetBannedLetter(string.IsNullOrEmpty(letter) ? null : letter[0]);
            state.RaiseBenchChanged();
            return Result.Success;
        }

        private Result BenchSetBayFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (!TryBench(caller, state, out var bench, out var err)) return err;
            if (Deserialize<BenchBayPayload>(payloadJson) is not { } p) return Result.FromError("Malformed bench-set-bay payload.");
            var cards = p.CardIds
                .Select(id => Enum.TryParse<ModifierId>(id, out var m) ? m : ModifierId.Unknown)
                .Where(m => m != ModifierId.Unknown)
                .ToList();
            bench.SetBay(p.PlayerId, cards);
            state.RaiseBenchChanged();
            return Result.Success;
        }

        private Result BenchSetScoreFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (!TryBench(caller, state, out var bench, out var err)) return err;
            if (Deserialize<BenchScorePayload>(payloadJson) is not { } p) return Result.FromError("Malformed bench-set-score payload.");
            bench.SetScore(p.PlayerId, p.Score);
            state.RaiseBenchChanged();
            return Result.Success;
        }

        private async Task<Result> BenchSubmitFromPayload(User caller, AlphaChainGameState state, string? payloadJson)
        {
            if (!TryBench(caller, state, out var bench, out var err)) return err;
            if (Deserialize<BenchSubmitPayload>(payloadJson) is not { } p) return Result.FromError("Malformed bench-submit payload.");

            var outcome = await bench.SubmitAsync(p.Word, p.RemainingSeconds);
            state.RaiseBenchChanged();
            if (outcome.TryGetFailure(out var error)) return error;
            if (!outcome.TryGetSuccess(out var result)) return Result.Success;
            // Surface a rejection as a Result error (shown inline); accepted plays are a success and
            // the projected score-replay strip reports the points.
            return result switch
            {
                SubmitWordResult.Accepted or SubmitWordResult.AcceptedZeroPointTax => Result.Success,
                SubmitWordResult.RejectedNotYourTurn => Result.FromError("It's not that player's turn."),
                SubmitWordResult.RejectedChainBroken c => Result.FromError($"Word must start with '{char.ToUpperInvariant(c.Required)}'."),
                SubmitWordResult.RejectedNotInDictionary => Result.FromError("Not a valid word (letters only)."),
                SubmitWordResult.RejectedDuplicate => Result.FromError("That word has already been played."),
                SubmitWordResult.RejectedEmpty => Result.FromError("Enter a word."),
                _ => Result.Success
            };
        }

        private async Task<Result> BenchSkip(User caller, AlphaChainGameState state)
        {
            if (!TryBench(caller, state, out var bench, out var err)) return err;
            var result = await bench.SkipTurnAsync();
            state.RaiseBenchChanged();
            return result;
        }

        /// <summary>Host-only + bench-active gate shared by the in-bench commands.</summary>
        private static bool TryBench(User caller, AlphaChainGameState state, out AlphaChainBenchScenario bench, out Result error)
        {
            bench = null!;
            if (caller.Id != state.Host.Id) { error = Result.FromError("Only the host can drive the Testing Bay."); return false; }
            if (state.Bench is not { } b) { error = Result.FromError("The Testing Bay is not open."); return false; }
            bench = b;
            error = Result.Success;
            return true;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        /// <summary>
        /// Alpha Chain counts the host as a participant when <c>HostPlays</c> is on, so
        /// readiness is gated on <c>Participants.Length</c> rather than the base check's
        /// <c>Players.Length</c>.
        /// </summary>
        public override Task<bool> CanStartAsync(AbstractGameState state, CancellationToken ct = default)
        {
            int count = state.Participants.Length;
            bool valid = MinPlayerCount <= count && count <= MaxPlayerCount && state.IsJoinable;
            return Task.FromResult(valid);
        }

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new AlphaChainGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            gameState.SubscribePlayerUnregistered(user => HandlePlayerLeft(user, gameState));
            // The Testing Bay's throwaway inner state holds its own lock/synthetic players; dispose it
            // when the lobby state tears down (host leaves / grace expires). Dispose() is non-virtual,
            // so this disposed-subscription is the correct teardown hook.
            gameState.SubscribeStateDisposed(() => gameState.Bench?.Dispose());
            logger.LogInformation("Created Alpha Chain gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        /// <summary>
        /// Returns the game to the lobby (host-only, terminal-phase-only) via the base
        /// <see cref="AbstractGameEngine{TState}.ReturnToLobby"/>. Flipping the state back to
        /// joinable re-renders every player's page at the lobby — no navigation needed.
        /// </summary>
        protected override bool IsTerminalPhase(AlphaChainGameState state) => state.Phase == AlphaChainGamePhase.GameOver;

        /// <inheritdoc />
        protected override void ResetForLobby(AlphaChainGameState state)
        {
            // SetupState re-snapshots GamePlayers from the roster on the next start, so
            // clearing the per-match state here is sufficient. Settings are preserved. Dropping the
            // Context also drops its EvaluationServices (the room-scoped card-state services); the next
            // StartAsyncCore builds a fresh Context + services, so no card state leaks across matches.
            state.Context = null;
            state.GamePlayers.Clear();
            state.TurnManager.TurnOrder.Clear();
            state.OptimizationSubmissions.Clear();
            state.PlayedWords.Clear();
            state.SubmissionHistory = System.Collections.Immutable.ImmutableList<AlphaChainSubmission>.Empty;
            state.CurrentRound = 0;
            state.CurrentEra = 0;
            state.IntermissionPhase = default;
            state.CurrentTutorial = default;
            state.ShownTutorials.Clear();
            state.SniperBanUserId = null;
            state.LatestScoreReplay = null;
            state.ScoreReplaySequence = 0;
            state.PendingTransitionAt = null;
            state.PendingTransitionIsGameOver = false;
            state.LastWord = null;
            state.RequiredStartLetter = null;
            state.BannedLetter = null;
            state.RoundLeaderUserId = null;
            state.LatestEngineNotices = [];
            state.EngineNoticeSequence = 0;
            state.Results = null;
            state.SetPhase(AlphaChainGamePhase.Setup);
        }

        protected override Task<Result> StartAsyncCore(AlphaChainGameState gameState, CancellationToken ct = default)
        {
            // Validate() is the single source of truth for a legal config (the lobby gates its
            // start buttons on the same call). Refuse to start an illegal match so a stale or
            // tampered config can never reach the FSM.
            var validation = gameState.Settings.Validate();
            if (!validation.IsValid)
            {
                logger.LogError("Refused to start Alpha Chain with invalid settings: {Violations}", validation.Summary);
                return Task.FromResult(Result.FromError(
                    "The game settings are invalid. " + validation.Summary,
                    "AlphaChainSettings.Validate reported: " + validation.Summary));
            }

            var context = new AlphaChainGameContext(gameState, this, wordList, rng, evaluator, modifierFactory, logger);
            var fsm = new FiniteStateMachine<AlphaChainGameContext, AlphaChainCommand>(logger);
            context.Fsm = fsm;

            var executeResult = gameState.Execute(() =>
            {
                // Single place that fixes host participation for the match. Must run before
                // we read Participants below so the host is in/out of the roster correctly.
                gameState.SetHostIsParticipant(gameState.Settings.HostPlays);
                gameState.Context = context;
                gameState.SetJoinable(false);

                // Build the turn order from Participants (includes the host when HostPlays).
                gameState.TurnManager.TurnOrder.Clear();
                foreach (var entry in gameState.Participants)
                    gameState.TurnManager.TurnOrder.Add(entry.User.Id);

                // SetupState.OnEnter snapshots GamePlayers and immediately chains to RoundState.
                fsm.TransitionTo(context, new SetupState());
            });

            if (executeResult.TryGetFailure(out var error)) return Task.FromResult<Result>(error);
            return Task.FromResult(Result.Success);
        }

        // ── Command dispatch ─────────────────────────────────────────────────

        /// <summary>
        /// Gateway used by Razor pages. Serializes via <c>ExecuteAsync</c> and delegates
        /// to the FSM's current state.
        /// </summary>
        public async Task<Result> ProcessCommandAsync(AlphaChainGameState state, AlphaChainCommand command)
        {
            if (state.Context is null)
                return Result.FromError("The game has not been started yet.");

            var context = state.Context;
            Result commandResult = Result.Success;

            var executeResult = await state.ExecuteAsync(() =>
            {
                var fsmResult = context.Fsm.HandleCommand(context, command);
                if (fsmResult.TryGetFailure(out var err))
                {
                    logger.LogError("Alpha Chain FSM command error: {msg}", err.PublicMessage);
                    commandResult = Result.FromError(err.PublicMessage, err.InternalMessage);
                }
                return ValueTask.CompletedTask;
            }, ct: default);

            if (executeResult.TryGetFailure(out var execErr)) return execErr;
            return commandResult;
        }

        /// <summary>Convenience wrapper for the UI: advances the active player's turn.</summary>
        public Task<Result> AdvanceTurnAsync(Guid actorUserId, AlphaChainGameState state)
            => ProcessCommandAsync(state, new AdvanceTurnCommand(actorUserId));

        /// <summary>Convenience wrapper for the UI: commits an Engine Bay ordering during Intermission Optimization.</summary>
        public Task<Result> SubmitOptimizationAsync(Guid actorUserId, IReadOnlyList<string> modifierBayIds, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SubmitOptimizationCommand(actorUserId, modifierBayIds));

        /// <summary>Convenience wrapper for the UI: the last-place player picks the next era's banned letter.</summary>
        public Task<Result> SelectSniperBanAsync(Guid actorUserId, char letter, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SelectSniperBanCommand(actorUserId, letter));

        /// <summary>Convenience wrapper for the UI: the host skips the currently-showing tutorial.</summary>
        public Task<Result> SkipTutorialAsync(Guid actorUserId, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SkipTutorialCommand(actorUserId));

        /// <summary>
        /// Submits a word for <paramref name="actorUserId"/> and returns the typed
        /// <see cref="SubmitWordResult"/> the UI renders. The FSM computes the result inside
        /// the execute lock (uniqueness, chain, dictionary, scoring, turn advance) and stashes
        /// it on the context; this reads it back out after the dispatch completes.
        /// </summary>
        public async Task<ValueResult<SubmitWordResult>> SubmitWordAsync(
            Guid actorUserId, string wordRaw, AlphaChainGameState state, DateTimeOffset? now = null)
        {
            if (state.Context is null)
                return ValueResult<SubmitWordResult>.FromError("The game has not been started yet.");

            var context = state.Context;
            SubmitWordResult? result = null;
            var submittedAt = now ?? DateTimeOffset.UtcNow;

            var executeResult = await state.ExecuteAsync(() =>
            {
                context.LastSubmitResult = null;
                var fsmResult = context.Fsm.HandleCommand(context, new SubmitWordCommand(actorUserId, wordRaw, submittedAt));
                if (fsmResult.TryGetFailure(out var err))
                    logger.LogError("Alpha Chain submit-word FSM error: {msg}", err.PublicMessage);
                result = context.LastSubmitResult;
                return ValueTask.CompletedTask;
            }, ct: default);

            if (executeResult.TryGetFailure(out var execErr))
                return ValueResult<SubmitWordResult>.FromError(execErr);

            if (result is null)
                return ValueResult<SubmitWordResult>.FromError(
                    "The word could not be processed right now.",
                    "RoundState did not produce a SubmitWordResult (wrong phase or null current state).");

            return result;
        }

        /// <summary>
        /// Drives time-based transitions. Call periodically from a host tick. No-op in M1
        /// except for recording <c>PhaseEndTime</c> in <c>RoundState.OnEnter</c>.
        /// </summary>
        public Result Tick(AlphaChainGameContext context, DateTimeOffset now)
        {
            var executeResult = context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.Tick(context, now);
                if (fsmResult.TryGetFailure(out var err))
                {
                    logger.LogError("Alpha Chain FSM tick error: {msg}", err.PublicMessage);
                    return Result.FromError(err.PublicMessage, err.InternalMessage);
                }
                return Result.Success;
            });

            if (!executeResult.IsSuccess) return executeResult.Error.Error;
            return executeResult.Value;
        }

        // ── Player-leave handling ────────────────────────────────────────────

        /// <summary>
        /// Subscribed in <see cref="CreateStateAsync"/>; fires outside the execute lock.
        /// Marks the player as left and, if it was their turn, advances so the game does
        /// not stall on a departed player.
        /// </summary>
        internal void HandlePlayerLeft(User user, AlphaChainGameState state)
        {
            // No game in progress (or already torn down) → nothing to fix.
            if (state.Context is null || state.IsDisposed) return;

            state.Execute(() =>
            {
                if (state.GamePlayers.TryGetValue(user.Id, out var ps))
                {
                    ps.HasLeft = true;
                    // In Survival mode a departure is also an elimination — the player is
                    // out for good, mirroring the timeout consequence.
                    if (state.Settings.SurvivalMode)
                        state.MarkEliminated(ps);
                }

                // If the departing player held the turn, advance past them.
                if (state.TurnManager.CurrentPlayer.GetValueOrDefault() == user.Id)
                    state.TurnManager.NextTurn();

                // End the match when no one is left to play: an empty field ends it in any mode
                // (there's no active seat to advance to), and Survival also ends on a lone
                // survivor. This keeps the FSM from limping along on a departed turn-holder.
                int active = CountActivePlayers(state);
                if (state.Phase != AlphaChainGamePhase.GameOver
                    && (active == 0 || (state.Settings.SurvivalMode && active < 2)))
                {
                    state.Context.Fsm.TransitionTo(state.Context, new GameOverState());
                }

                logger.LogInformation(
                    "Alpha Chain player [{userId}] left. Active player is now [{active}].",
                    user.Id, state.TurnManager.CurrentPlayer);
            });
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
