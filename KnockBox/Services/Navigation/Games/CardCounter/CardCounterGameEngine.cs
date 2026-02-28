using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Navigation.Games.CardCounter
{
    public class CardCounterGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<CardCounterGameEngine> logger,
        ILogger<CardCounterGameState> stateLogger) : AbstractGameEngine
    {
        public override async Task<Result<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Result.FromError<AbstractGameState>(new ArgumentNullException(nameof(host)));

            var gameState = new CardCounterGameState(host, stateLogger, randomNumberService);
            gameState.UpdateJoinableStatus(true);
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Result.FromValue<AbstractGameState>(gameState);
        }

        public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not CardCounterGameState gameState)
                return Result.FromError(new InvalidCastException($"Game state of type [{state.GetType().Name}] couldn't be cast to type [{nameof(CardCounterGameState)}]."));

            if (host != gameState.Host)
                return Result.FromError(new InvalidOperationException($"Only the host can start the game."));

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
                gameState.InitializeGame();
            });

            if (executeResult.IsFailure) return executeResult;
            return Result.Success;
        }
    }
}