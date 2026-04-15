using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class RoundSetupPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;

        private List<SecretTask>? PlayerTasks => UserService.CurrentUser != null && GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var p)
            ? p.SecretTasks
            : null;
    }
}
