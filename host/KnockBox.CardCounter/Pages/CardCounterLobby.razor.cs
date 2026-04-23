using KnockBox.Core.Components.Shared;
using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.CardCounter.Pages
{
    public partial class CardCounterLobby : LobbyPageBase<CardCounterGameState>
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        private const int ShoeAnimationDurationMs = 2500;
        private int _prevShoeIndex = -1;
        protected bool IsAnimatingShoe { get; private set; }

        protected override Task OnLobbyInitializedAsync()
        {
            _prevShoeIndex = GameState.ShoeIndex;
            return Task.CompletedTask;
        }

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

        protected override async ValueTask OnStateChangedAsync()
        {
            bool isNewShoe = false;

            if (GameState is not null && GameState.ShoeIndex < _prevShoeIndex)
            {
                // Game was restarted — ShoeIndex reset to 0; sync baseline so future increments are detected.
                _prevShoeIndex = GameState.ShoeIndex;
            }

            if (GameState is not null && GameState.ShoeIndex > _prevShoeIndex)
            {
                isNewShoe = true;
                _prevShoeIndex = GameState.ShoeIndex;
                IsAnimatingShoe = true;
            }

            await InvokeAsync(StateHasChanged);

            if (isNewShoe)
            {
                await Task.Delay(ShoeAnimationDurationMs);
                IsAnimatingShoe = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }
}
