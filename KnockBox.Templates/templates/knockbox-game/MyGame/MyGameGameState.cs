using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace MyGame;

public class MyGameGameState(User host, ILogger<MyGameGameState> logger)
    : AbstractGameState(host, logger)
{
    // TODO: Add your game-specific state properties here
}
