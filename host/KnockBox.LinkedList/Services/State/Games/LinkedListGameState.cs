using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.LinkedList.Services.State.Games
{
    public class LinkedListGameState(
        User host,
        ILogger<LinkedListGameState> logger)
        : AbstractGameState(host, logger)
    {
        // Per-room game state goes here.
        // All mutation must go through Execute / ExecuteAsync.
    }
}
