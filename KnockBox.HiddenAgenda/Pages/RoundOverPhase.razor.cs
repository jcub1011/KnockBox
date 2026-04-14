using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Extensions.Returns;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class RoundOverPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameEngine Engine { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;

        private bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        private void HandleNextRound()
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.StartNextRound(UserService.CurrentUser, GameState);
            // Handle error if needed
        }
    }
}
