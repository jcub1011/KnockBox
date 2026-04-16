using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;

namespace KnockBox.Plugin.Services.State
{
    public class GameState(User host, ILogger logger) : AbstractGameState(host, logger)
    {
        // Add your game-specific state properties here.
    }
}
