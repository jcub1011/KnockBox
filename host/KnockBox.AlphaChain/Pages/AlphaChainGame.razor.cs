using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.Core.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain.Pages
{
    public partial class AlphaChainGame : DisposableComponent
    {
        [Inject] protected AlphaChainGameEngine GameEngine { get; set; } = default!;
        [Inject] protected ILogger<AlphaChainGame> Logger { get; set; } = default!;

        [Parameter] public AlphaChainGameState GameState { get; set; } = default!;

        private IDisposable? _stateSubscription;

        /// <summary>Display name of the active player, or a placeholder when unset.</summary>
        protected string CurrentPlayerName
        {
            get
            {
                var id = GameState.TurnManager.CurrentPlayer;
                if (id is not null && GameState.GamePlayers.TryGetValue(id, out var ps))
                    return ps.DisplayName;
                return "—";
            }
        }

        protected override void OnInitialized()
        {
            // The parent lobby (LobbyPageBase) also re-renders on state changes, but
            // subscribing here keeps this view self-contained and correct in isolation.
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(
                async () => await InvokeAsync(StateHasChanged));
        }

        /// <summary>
        /// Debug control: advances the turn as the <i>current</i> player so a single
        /// window (e.g. a shared display) can drive the loop while verifying M1.
        /// </summary>
        protected async Task AdvanceTurnAsync()
        {
            var currentId = GameState.TurnManager.CurrentPlayer;
            if (currentId is null) return;

            var result = await GameEngine.AdvanceTurnAsync(currentId, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Failed to advance turn: {Error}", error.PublicMessage);
        }

        public override void Dispose()
        {
            _stateSubscription?.Dispose();
            base.Dispose();
        }
    }
}
