using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Primitives.Returns;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class MatchOverPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameEngine Engine { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;

        private bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        private HiddenAgendaPlayerState? Winner => GameState.GamePlayers.Values.OrderByDescending(p => p.CumulativeScore).FirstOrDefault();
        private string WinnerName => Winner?.DisplayName ?? "No one";
        private int WinnerScore => Winner?.CumulativeScore ?? 0;

        private void HandleReturnToLobby()
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.ReturnToLobby(UserService.CurrentUser, GameState);
            // Handle error if needed
        }

        private void HandleExitGame()
        {
            // The host project handles exiting via navigation to home
        }
    }
}
