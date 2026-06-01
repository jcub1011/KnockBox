using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.AlphaChain.Services.Logic.Games
{
    /// <summary>
    /// Server-authoritative, FSM-driven engine for Alpha Chain. The engine is a
    /// singleton; all mutable per-room state lives on <see cref="AlphaChainGameState"/>
    /// (and its <see cref="AlphaChainGameContext"/>).
    /// </summary>
    public class AlphaChainGameEngine(
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
            var context = new AlphaChainGameContext(gameState, this, logger);
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
                    ps.HasLeft = true;

                // If the departing player held the turn, advance past them.
                if (state.TurnManager.CurrentPlayer == user.Id)
                    state.TurnManager.NextTurn();

                logger.LogInformation(
                    "Alpha Chain player [{userId}] left. Active player is now [{active}].",
                    user.Id, state.TurnManager.CurrentPlayer);
            });
        }
    }
}
