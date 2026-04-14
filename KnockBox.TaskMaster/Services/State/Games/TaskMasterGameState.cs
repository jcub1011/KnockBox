using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.TaskMaster.Services.State.Games
{
    public class TaskMasterGameState(
        User host,
        ILogger<TaskMasterGameState> logger)
        : AbstractGameState(host, logger),
          IPhasedGameState<GamePhase>
    {
        public GamePhase Phase { get; private set; }

        public void SetPhase(GamePhase phase)
        {
            Phase = phase;
        }
    }

    public enum GamePhase
    {
        Lobby,
        Playing,
        GameOver
    }
}
