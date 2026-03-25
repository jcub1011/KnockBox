using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Server-authoritative engine for Drawn To Dress.
    /// The engine is a singleton; all mutable game state lives in
    /// <see cref="DrawnToDressGameState"/> (and its <see cref="DrawnToDressGameContext"/>),
    /// which is created per game session.
    /// </summary>
    public class DrawnToDressGameEngine(
        ILogger<DrawnToDressGameEngine> logger,
        ILogger<DrawnToDressGameState> stateLogger) : AbstractGameEngine
    {
        // ── AbstractGameEngine lifecycle ──────────────────────────────────────

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new DrawnToDressGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);

            // Create the context and FSM so the lobby state is active from the start.
            var context = new DrawnToDressGameContext(gameState, logger);
            var fsm = new FiniteStateMachine<DrawnToDressGameContext, DrawnToDressCommand>(logger);
            context.Fsm = fsm;
            gameState.Context = context;
            fsm.TransitionTo(context, new LobbyState());

            logger.LogInformation("Created DrawnToDress state with host [{id}].", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        public override Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not DrawnToDressGameState gameState)
                return Task.FromResult(Result.FromError("Error starting game.",
                    $"Game state of type [{state?.GetType().Name ?? "null"}] couldn't be cast to [{nameof(DrawnToDressGameState)}]."));

            if (host != gameState.Host)
                return Task.FromResult(Result.FromError("Only the host can start the game."));

            if (gameState.Context is null)
                return Task.FromResult(Result.FromError("Game context is not initialized."));

            var context = gameState.Context;

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);

                // Snapshot all registered players into GamePlayers so FSM states can look
                // them up by ID.  This mirrors the CardCounter pattern and must happen before
                // the FSM transitions so that commands processed on the very first tick
                // (e.g. SubmitDrawingCommand) find their player state.
                foreach (var player in gameState.Players)
                {
                    gameState.GamePlayers[player.Id] = new DrawnToDressPlayerState
                    {
                        PlayerId = player.Id,
                        DisplayName = player.Name,
                    };
                }

                // Transition from LobbyState → ThemeSelectionState.
                // ThemeSelectionState will chain immediately to DrawingRoundState when
                // ThemeSource is Random (the default).
                context.Fsm.TransitionTo(context, new ThemeSelectionState());
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }

        // ── FSM core ──────────────────────────────────────────────────────────

        /// <summary>
        /// Processes a player command by delegating to the current FSM state inside the
        /// game's execute lock. State transitions are handled automatically.
        /// </summary>
        public Result ProcessCommand(DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            return context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.HandleCommand(context, command);
                if (fsmResult.TryGetFailure(out var err))
                    logger.LogError("FSM command error: {msg}", err.PublicMessage);
            });
        }

        /// <summary>
        /// Drives time-based transitions (e.g., drawing timer, outfit building timer).
        /// Call periodically from a timer or background service.
        /// </summary>
        public Result Tick(DrawnToDressGameContext context, DateTimeOffset now)
        {
            return context.State.Execute(() =>
            {
                var fsmResult = context.Fsm.Tick(context, now);
                if (fsmResult.TryGetFailure(out var err))
                    logger.LogError("FSM tick error: {msg}", err.PublicMessage);
            });
        }

        // ── Player-leave handling ─────────────────────────────────────────────

        /// <summary>
        /// Called whenever a player unregisters from the game (disconnect, tab close, or kick).
        /// </summary>
        internal void HandlePlayerLeft(User player, DrawnToDressGameState state)
        {
            logger.LogInformation("Player [{id}] left DrawnToDress game hosted by [{hostId}].",
                player.Id, state.Host.Id);

            // TODO: Handle active-player removal during drawing/voting phases in later issues.
        }
    }
}
