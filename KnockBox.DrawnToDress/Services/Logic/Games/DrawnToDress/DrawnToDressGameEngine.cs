using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Server-authoritative, event-driven FSM engine for Drawn to Dress.
    /// The engine is a singleton; all mutable game state lives in
    /// <see cref="DrawnToDressGameState"/> (and its <see cref="DrawnToDressGameContext"/>),
    /// which is created per game session.
    /// </summary>
    public class DrawnToDressGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<DrawnToDressGameEngine> logger,
        ILogger<DrawnToDressGameState> stateLogger) : AbstractGameEngine
    {
        // ── AbstractGameEngine lifecycle ──────────────────────────────────────

        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.",
                    $"Parameter {nameof(host)} was null.");

            var state = new DrawnToDressGameState(host, stateLogger);
            state.UpdateJoinableStatus(true);
            logger.LogInformation("Created DrawnToDress state with host [{id}].", host.Id);
            return state;
        }

        public override async Task<Result> StartAsync(
            User host, AbstractGameState abstractState, CancellationToken ct = default)
        {
            if (abstractState is not DrawnToDressGameState state)
                return Result.FromError(
                    "Error starting game.",
                    $"State type [{abstractState?.GetType().Name ?? "null"}] is not {nameof(DrawnToDressGameState)}.");

            if (host != state.Host)
                return Result.FromError("Only the host can start the game.");

            if (state.Players.Count < state.Settings.MinPlayers)
                return Result.FromError(
                    $"At least {state.Settings.MinPlayers} players are required to start.");

            var context = new DrawnToDressGameContext(state, randomNumberService, logger);
            var fsm = new FiniteStateMachine<DrawnToDressGameContext, DrawnToDressCommand>(logger);
            context.Fsm = fsm;

            // TransitionTo is included inside Execute so that StateChangedEventManager.Notify()
            // fires *after* both the joinable-status update and the phase change are committed,
            // ensuring all connected clients see the Drawing phase in a single re-render.
            return state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
                state.Context = context;
                fsm.TransitionTo(context, new DrawingState());
            });
        }

        // ── FSM core ──────────────────────────────────────────────────────────

        /// <summary>
        /// Processes a player command by delegating to the current FSM state inside the
        /// game's execute lock. State transitions and validation errors are handled automatically.
        /// </summary>
        public Result ProcessCommand(DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> fsmResult = default;

            var executeResult = context.State.Execute(() =>
            {
                fsmResult = context.Fsm.HandleCommand(context, command);
            });

            if (executeResult.IsFailure) return executeResult;

            if (fsmResult.IsFailure && fsmResult.TryGetFailure(out var fsmError))
                return Result.FromError(fsmError);

            return Result.Success;
        }

        /// <summary>
        /// Advances time-based transitions (drawing sub-round timer, outfit-building timer).
        /// Should be called periodically (e.g. every second) from any connected client.
        /// The FSM's internal deadline check ensures the auto-advance fires exactly once.
        /// </summary>
        public Result Tick(DrawnToDressGameState state, DateTimeOffset now)
        {
            if (state.Context is null) return Result.Success;
            var context = state.Context;

            ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> fsmResult = default;

            var executeResult = context.State.Execute(() =>
            {
                fsmResult = context.Fsm.Tick(context, now);
            });

            if (executeResult.IsFailure) return executeResult;

            if (fsmResult.IsFailure && fsmResult.TryGetFailure(out var fsmError))
                return Result.FromError(fsmError);

            return Result.Success;
        }

        // ── Public UI-facing methods ──────────────────────────────────────────

        // Drawing phase

        /// <summary>A player submits a drawing for the current clothing type.</summary>
        public Result SubmitDrawing(User player, DrawnToDressGameState state, string svgData)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitDrawingCommand(player.Id, svgData));
        }

        /// <summary>Host advances to the next clothing type, or ends the drawing phase.</summary>
        public Result AdvanceDrawingRound(User host, DrawnToDressGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new AdvanceDrawingRoundCommand(host.Id));
        }

        // Outfit building phase

        /// <summary>A player claims an item from the shared pool. First claim wins.</summary>
        public Result ClaimItem(User player, DrawnToDressGameState state, Guid itemId)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new ClaimItemCommand(player.Id, itemId));
        }

        /// <summary>A player returns a previously claimed item back to the pool.</summary>
        public Result ReturnItem(User player, DrawnToDressGameState state, Guid itemId, ClothingType slotType)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new ReturnItemCommand(player.Id, itemId, slotType));
        }

        /// <summary>A player locks their outfit. Once locked, picks cannot be changed.</summary>
        public Result LockOutfit(User player, DrawnToDressGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new LockOutfitCommand(player.Id));
        }

        /// <summary>Host ends the outfit building phase and moves all players to customization.</summary>
        public Result EndOutfitBuilding(User host, DrawnToDressGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new EndOutfitBuildingCommand(host.Id));
        }

        // Outfit customization phase

        /// <summary>A player submits their outfit with a name (and optional sketch).</summary>
        public Result SubmitOutfit(User player, DrawnToDressGameState state, string name, string? sketchData = null)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new SubmitOutfitCommand(player.Id, name, sketchData));
        }

        /// <summary>Host ends the customization phase and either starts the next outfit round or begins voting.</summary>
        public Result EndCustomizationPhase(User host, DrawnToDressGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new EndCustomizationCommand(host.Id));
        }

        // Voting phase

        /// <summary>
        /// Casts a vote for each criterion in the current voting round's matchup.
        /// <paramref name="votes"/> maps criterion → true if voting for Outfit A, false for Outfit B.
        /// </summary>
        public Result CastVote(
            User player,
            DrawnToDressGameState state,
            Guid matchupId,
            Dictionary<VotingCriterion, bool> votes)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new CastVoteCommand(player.Id, matchupId, votes));
        }

        /// <summary>Host finalizes the current voting round; tallies points and advances (or ends) the tournament.</summary>
        public Result FinalizeVotingRound(User host, DrawnToDressGameState state)
        {
            if (!TryGetContext(state, out var ctx, out var err)) return err;
            return ProcessCommand(ctx, new FinalizeVotingRoundCommand(host.Id));
        }

        /// <summary>
        /// Resets the game to the Lobby phase so the same group can play again.
        /// Settings and the player/host roster are preserved; all game data is cleared.
        /// </summary>
        public Result ResetToLobby(User host, DrawnToDressGameState state)
        {
            if (state is null)
                return Result.FromError("Game state was null.");

            if (host != state.Host)
                return Result.FromError("Only the host can restart the game.");

            var executeResult = state.Execute(() =>
            {
                state.ResetForNewGame();
                state.UpdateJoinableStatus(true);
            });

            if (executeResult.IsFailure) return executeResult;

            logger.LogInformation("DrawnToDress game [{id}] reset to Lobby by host [{hostId}].",
                state.Host.Id, host.Id);
            return Result.Success;
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static bool TryGetContext(
            DrawnToDressGameState state,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DrawnToDressGameContext? context,
            out Result error)
        {
            if (state.Context is null)
            {
                context = null;
                error = Result.FromError("The game has not been started yet.");
                return false;
            }
            context = state.Context;
            error = default;
            return true;
        }
    }
}
