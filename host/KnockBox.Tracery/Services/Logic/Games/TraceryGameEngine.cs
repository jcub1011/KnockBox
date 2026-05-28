using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Tracery.Services.Logic.Games
{
    public class TraceryGameEngine(
        ILogger<TraceryGameEngine> logger,
        ILogger<TraceryGameState> stateLogger) : AbstractGameEngine(2, 8)
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new TraceryGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created Tracery gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not TraceryGameState gameState)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{state?.GetType().Name ?? "null"}] couldn't be cast to type [{nameof(TraceryGameState)}]."));

            var executeResult = gameState.Execute(() =>
            {
                gameState.SetJoinable(false);
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }
    }
}
