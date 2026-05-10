using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Pages.Components;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages
{
    public partial class DndMapperLobby : ComponentBase
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string RoomCode { get; set; } = string.Empty;

        [Inject] protected DndMapperGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;

        private readonly DndMapperToastService _toasts = new();

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await GameEngine.StartAsync(UserService.CurrentUser, State);
        }
    }
}
