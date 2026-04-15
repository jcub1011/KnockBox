using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class LobbyPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameEngine Engine { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameConfig Config { get; set; } = default!;
        [Parameter, EditorRequired] public string RoomCode { get; set; } = default!;

        private bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        private async Task KickPlayer(User player)
        {
            await GameState.ExecuteAsync(() =>
            {
                GameState.KickPlayer(player);
                return ValueTask.CompletedTask;
            });
        }

        private async Task StartGame()
        {
            await Engine.StartAsync(UserService.CurrentUser!, GameState);
        }
    }
}
