using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.TaskMaster.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.TaskMaster.Services.Logic.Games
{
    public class TaskMasterGameEngine(
        ILogger<TaskMasterGameEngine> logger,
        ILogger<TaskMasterGameState> stateLogger) : AbstractGameEngine<TaskMasterGameState>
    {
        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null.");

            var gameState = new TaskMasterGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            gameState.SubscribePlayerUnregistered(player => HandlePlayerLeft(player, gameState));
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return gameState;
        }

        protected override Task<Result> StartAsyncCore(TaskMasterGameState state, CancellationToken ct = default)
        {
            var executeResult = state.Execute(() =>
            {
                state.SetJoinable(false);
                state.SetPhase(GamePhase.Playing);
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }

        internal void HandlePlayerLeft(User player, TaskMasterGameState state)
        {
            logger.LogInformation("Player [{playerId}] left the game.", player.Id);
        }
    }
}
