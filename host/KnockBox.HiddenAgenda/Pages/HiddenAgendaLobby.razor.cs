using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.PlayLog;
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
            await GameEngine.StartAsync(UserService.CurrentUser, GameState);
        }

        protected override GameLog? BuildEndOfGamePlayLog()
        {
            if (GameState.Phase != GamePhase.MatchOver)
                return null;

            var metadata = HiddenAgendaPlayLogMetadata.Build(GameState, UserService.CurrentUser?.Id);
            return GameLog.Create("hidden-agenda", metadata);
        }
    }
}
