using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Navigation.Games.DiceSimulator
{
    public class DiceSimulatorGameEngine(
        IRandomNumberService randomNumberservice,
        ILogger<DiceSimulatorGameEngine> logger,
        ILogger<DiceSimulatorGameState> stateLogger) : AbstractGameEngine
    {
        public override async Task<Result<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            var gameState = new DiceSimulatorGameState(host, stateLogger, randomNumberservice);
            return Result.FromValue<AbstractGameState>(gameState);
        }

        public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not DiceSimulatorGameState gameState)
                return Result.FromError(new InvalidCastException($"Game state of type [{state.GetType().Name}] couldn't be cast to type [{nameof(DiceSimulatorGameState)}]."));

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
            });

            if (executeResult.IsFailure) return executeResult;
            return Result.Success;
        }
    }
}
