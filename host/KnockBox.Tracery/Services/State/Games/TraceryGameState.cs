using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Tracery.Services.State.Games
{
    public class TraceryGameState(
        User host,
        ILogger<TraceryGameState> logger)
        : AbstractGameState(host, logger)
    {
    }
}
