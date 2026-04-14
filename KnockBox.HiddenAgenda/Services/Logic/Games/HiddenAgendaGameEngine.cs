using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.HiddenAgenda.Services.Logic.Games
{
    public class HiddenAgendaGameEngine(
        ILogger<HiddenAgendaGameEngine> logger,
        ILogger<HiddenAgendaGameState> stateLogger) : AbstractGameEngine
    {
        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null.");

            var gameState = new HiddenAgendaGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            gameState.PlayerUnregistered += player => HandlePlayerLeft(player, gameState);
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return gameState;
        }

        public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not HiddenAgendaGameState gameState)
                return Result.FromError("Error starting game.", $"Game state of type [{(state?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(HiddenAgendaGameState)}].");

            if (host != gameState.Host)
                return Result.FromError("Only the host can start the game.");

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
                gameState.SetPhase(GamePhase.Playing);
            });

            if (executeResult.IsFailure) return executeResult;
            return Result.Success;
        }

        internal void HandlePlayerLeft(User player, HiddenAgendaGameState state)
        {
            logger.LogInformation("Player [{playerId}] left the game.", player.Id);
        }
    }
}
