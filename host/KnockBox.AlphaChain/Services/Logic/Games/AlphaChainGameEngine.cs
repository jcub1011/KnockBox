using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
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
        ILogger<AlphaChainGameState> stateLogger) : AbstractGameEngine<AlphaChainGameState>(2, 8)
    {
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
            // clearing the per-match state here is sufficient. Settings are preserved.
            state.Context = null;
            state.GamePlayers.Clear();
            state.TurnManager.TurnOrder.Clear();
            state.OptimizationSubmissions.Clear();
            state.PlayedWords.Clear();
            state.PlayLog.Clear();
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
        public Task<Result> AdvanceTurnAsync(string actorUserId, AlphaChainGameState state)
            => ProcessCommandAsync(state, new AdvanceTurnCommand(actorUserId));

        /// <summary>Convenience wrapper for the UI: commits an Engine Bay ordering during Intermission Optimization.</summary>
        public Task<Result> SubmitOptimizationAsync(string actorUserId, IReadOnlyList<string> modifierBayIds, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SubmitOptimizationCommand(actorUserId, modifierBayIds));

        /// <summary>Convenience wrapper for the UI: the last-place player picks the next era's banned letter.</summary>
        public Task<Result> SelectSniperBanAsync(string actorUserId, char letter, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SelectSniperBanCommand(actorUserId, letter));

        /// <summary>Convenience wrapper for the UI: the host skips the currently-showing tutorial.</summary>
        public Task<Result> SkipTutorialAsync(string actorUserId, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SkipTutorialCommand(actorUserId));

        /// <summary>
        /// Submits a word for <paramref name="actorUserId"/> and returns the typed
        /// <see cref="SubmitWordResult"/> the UI renders. The FSM computes the result inside
        /// the execute lock (uniqueness, chain, dictionary, scoring, turn advance) and stashes
        /// it on the context; this reads it back out after the dispatch completes.
        /// </summary>
        public async Task<ValueResult<SubmitWordResult>> SubmitWordAsync(
            string actorUserId, string wordRaw, AlphaChainGameState state, DateTimeOffset? now = null)
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
                if (state.TurnManager.CurrentPlayer == user.Id)
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
