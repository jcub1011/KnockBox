using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.TaskMaster.Services.Logic.Games;
using KnockBox.TaskMaster.Services.State.Games;
using KnockBox.TaskMaster.Services.State.Games.PlayLog;
using Microsoft.AspNetCore.Components;

namespace KnockBox.TaskMaster.Pages
{
    public partial class TaskMasterLobby : LobbyPageBase<TaskMasterGameState>
    {
        [Inject] protected TaskMasterGameEngine GameEngine { get; set; } = default!;

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await GameEngine.StartAsync(UserService.CurrentUser, GameState);
        }

        protected override GameLog? BuildEndOfGamePlayLog()
        {
            if (GameState.Phase != GamePhase.GameOver)
                return null;

            return GameLog.Create(
                "task-master",
                TaskMasterPlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id));
        }
    }
}
