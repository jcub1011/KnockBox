using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.AlphaChain.Services.Logic.Games
{
    public class AlphaChainGameEngine(
        ILogger<AlphaChainGameEngine> logger,
        ILogger<AlphaChainGameState> stateLogger) : AbstractGameEngine<AlphaChainGameState>(2, 8)
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new AlphaChainGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created Alpha Chain gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        protected override Task<Result> StartAsyncCore(AlphaChainGameState gameState, CancellationToken ct = default)
        {
            var executeResult = gameState.Execute(() =>
            {
                gameState.SetJoinable(false);
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }
    }
}
