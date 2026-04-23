using KnockBox.Core.Components.Shared;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class HiddenAgendaLobby : LobbyPageBase<HiddenAgendaGameState>
    {
        [Inject] protected HiddenAgendaGameEngine GameEngine { get; set; } = default!;

        protected override bool TryGetHostTick(out Action action, out int tickInterval)
        {
            action = () =>
            {
                if (GameState?.Context is not null)
                    GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);
            };
            tickInterval = TickService.TicksPerSecond;
            return true;
        }

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            if (UserService.CurrentUser.Id != GameState.Host.Id) return;
            await GameEngine.StartAsync(GameState);
        }
    }
}
