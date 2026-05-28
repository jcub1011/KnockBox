using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;

namespace KnockBox.LinkedList.Services.Logic.Games
{
    public class LinkedListGameEngine(
        ILogger<LinkedListGameEngine> logger,
        ILogger<LinkedListGameState> stateLogger) : AbstractGameEngine
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new LinkedListGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not LinkedListGameState gameState)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{(state?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(LinkedListGameState)}]."));

            var executeResult = gameState.Execute(() =>
            {
                gameState.SetJoinable(false);
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }
    }
}
