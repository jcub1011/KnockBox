using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Server-authoritative engine for Drawn To Dress.
    /// The engine is a singleton; all mutable game state lives in
    /// <see cref="DrawnToDressGameState"/>.
    /// </summary>
    public class DrawnToDressGameEngine(
        ILogger<DrawnToDressGameEngine> logger,
        ILogger<DrawnToDressGameState> stateLogger) : AbstractGameEngine
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new DrawnToDressGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);
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

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
                gameState.SetPhase(GamePhase.Drawing);
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }

        internal void HandlePlayerLeft(User player, DrawnToDressGameState state)
        {
            logger.LogInformation("Player [{id}] left DrawnToDress game hosted by [{hostId}].",
                player.Id, state.Host.Id);
        }
    }
}
