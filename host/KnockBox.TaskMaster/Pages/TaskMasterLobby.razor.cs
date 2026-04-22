using KnockBox.Core.Components.Shared;
using KnockBox.TaskMaster.Services.Logic.Games;
using KnockBox.TaskMaster.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.TaskMaster.Pages
{
    public partial class TaskMasterLobby : LobbyPageBase<TaskMasterGameState>
    {
        [Inject] protected TaskMasterGameEngine GameEngine { get; set; } = default!;

        protected async Task StartGame()
        {
            await GameEngine.StartAsync(UserService.CurrentUser!, GameState);
        }
    }
}
