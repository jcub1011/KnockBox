using KnockBox.Core.Components.Shared;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class LinkedListLobby : LobbyPageBase<LinkedListGameState>
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await GameEngine.StartAsync(UserService.CurrentUser, GameState);
        }
    }
}
