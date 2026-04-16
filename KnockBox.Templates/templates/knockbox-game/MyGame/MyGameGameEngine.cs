using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace MyGame;

public class MyGameGameEngine(
    ILogger<MyGameGameEngine> logger,
    ILogger<MyGameGameState> stateLogger) : AbstractGameEngine(2, 8)
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default)
    {
        var state = new MyGameGameState(host, stateLogger);
        state.UpdateJoinableStatus(true);
        logger.LogInformation("Created game state for host [{HostId}].", host.Id);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    public override Task<Result> StartAsync(
        User host, AbstractGameState state, CancellationToken ct = default)
    {
        if (state is not MyGameGameState gameState)
            return Task.FromResult(Result.FromError("Invalid state type.", "Internal error."));

        if (host != gameState.Host)
            return Task.FromResult(Result.FromError("Only the host can start the game."));

        var executeResult = gameState.Execute(() =>
        {
            gameState.UpdateJoinableStatus(false);
            // TODO: Initialize your game state here
        });

        return Task.FromResult(executeResult);
    }
}
