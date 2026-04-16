using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Plugin.Services.State;
using Microsoft.Extensions.Logging;

namespace KnockBox.Plugin.Services.Logic
{
    public class GameEngine(ILogger<GameEngine> logger) : AbstractGameEngine(2, 8) // Min 2, Max 8 players
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            var state = new GameState(host, logger);
            return Task.FromResult(ValueResult<AbstractGameState>.Success(state));
        }

        public override Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not GameState gameState) return Task.FromResult(Result.Failure("Invalid game state."));
            
            // Logic to start the game
            return Task.FromResult(Result.Success());
        }
    }
}
