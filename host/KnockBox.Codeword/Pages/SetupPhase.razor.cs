using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword.Pages
{
    public partial class SetupPhase : ComponentBase
    {
        [Inject] protected IUserService UserService { get; set; } = default!;

        [Parameter] public CodewordGameState GameState { get; set; } = default!;

        private CodewordPlayerState? GetMyPlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }
    }
}

