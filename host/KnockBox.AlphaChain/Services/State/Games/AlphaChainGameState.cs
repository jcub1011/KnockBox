using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.AlphaChain.Services.State.Games
{
    public class AlphaChainGameState(
        User host,
        ILogger<AlphaChainGameState> logger)
        : AbstractGameState(host, logger)
    {
    }
}
