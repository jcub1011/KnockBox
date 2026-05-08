using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.DndMapper.Services.State.Games
{
    public class DndMapperGameState(
        User host,
        ILogger<DndMapperGameState> logger)
        : AbstractGameState(host, logger)
    {
    }
}
