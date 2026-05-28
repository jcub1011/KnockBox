using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Tracery.Pages
{
    public partial class TraceryLobby : LobbyPageBase<TraceryGameState>
    {
        [Inject] protected TraceryGameEngine GameEngine { get; set; } = default!;

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await GameEngine.StartAsync(UserService.CurrentUser, GameState);
        }
    }
}
