using KnockBox.Components.Shared;
using KnockBox.Services.Navigation.Games.DiceSimulator;
using KnockBox.Services.State.Games.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DiceSimulator
{
    public partial class DiceSimulatorLobby : DisposableComponent
    {
        [Inject] protected DiceSimulatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        public override void Dispose()
        {
            base.Dispose();
            GameSessionService.LeaveCurrentSession();
        }
    }
}
