using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain.Pages
{
    public partial class AlphaChainLobby : LobbyPageBase<AlphaChainGameState>
    {
        [Inject] protected AlphaChainGameEngine GameEngine { get; set; } = default!;

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await GameEngine.StartAsync(UserService.CurrentUser, GameState);
        }
    }
}
