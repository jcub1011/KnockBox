using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.Logic.Scoring;
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
        IScoreCalculator scoreCalculator,
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

            var context = new AlphaChainGameContext(gameState, this, wordList, rng, scoreCalculator, logger);
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

        /// <summary>Convenience wrapper for the UI: plays an action card from the actor's hand.</summary>
        public Task<Result> PlayActionAsync(string actorUserId, string cardId, string? targetUserId, AlphaChainGameState state)
            => ProcessCommandAsync(state, new PlayActionCommand(actorUserId, cardId, targetUserId));

        /// <summary>Convenience wrapper for the UI: re-orders the actor's Engine Bay.</summary>
        public Task<Result> ReorderEngineBayAsync(string actorUserId, IReadOnlyList<string> cardIds, AlphaChainGameState state)
            => ProcessCommandAsync(state, new ReorderEngineBayCommand(actorUserId, cardIds));

        /// <summary>Convenience wrapper for the host-only debug "Grant Cards" button.</summary>
        public Task<Result> GrantCardsAsync(string actorUserId, AlphaChainGameState state, int modifierCount = 2, int actionCount = 1)
            => ProcessCommandAsync(state, new GrantCardsDebugCommand(actorUserId, modifierCount, actionCount));

        /// <summary>Convenience wrapper for the UI: commits an Engine Bay ordering during Intermission Optimization.</summary>
        public Task<Result> SubmitOptimizationAsync(string actorUserId, IReadOnlyList<string> modifierBayIds, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SubmitOptimizationCommand(actorUserId, modifierBayIds));

        /// <summary>Convenience wrapper for the UI: the last-place player picks the next era's banned letter.</summary>
        public Task<Result> SelectSniperBanAsync(string actorUserId, char letter, AlphaChainGameState state)
            => ProcessCommandAsync(state, new SelectSniperBanCommand(actorUserId, letter));

        /// <summary>
        /// Submits a word for <paramref name="actorUserId"/> and returns the typed
        /// <see cref="SubmitWordResult"/> the UI renders. The FSM computes the result inside
        /// the execute lock (uniqueness, chain, dictionary, scoring, turn advance) and stashes
        /// it on the context; this reads it back out after the dispatch completes.
        /// </summary>
        public async Task<ValueResult<SubmitWordResult>> SubmitWordAsync(
            string actorUserId, string wordRaw, AlphaChainGameState state)
        {
            if (state.Context is null)
                return ValueResult<SubmitWordResult>.FromError("The game has not been started yet.");

            var context = state.Context;
            SubmitWordResult? result = null;

            var executeResult = await state.ExecuteAsync(() =>
            {
                context.LastSubmitResult = null;
                var fsmResult = context.Fsm.HandleCommand(context, new SubmitWordCommand(actorUserId, wordRaw));
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

                // Survival: if the field is down to a single active player, end the match.
                if (state.Settings.SurvivalMode
                    && state.Phase != AlphaChainGamePhase.GameOver
                    && CountActivePlayers(state) < 2)
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
