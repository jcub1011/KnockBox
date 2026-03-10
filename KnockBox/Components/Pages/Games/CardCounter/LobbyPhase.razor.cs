using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.CardCounter
{
    public partial class LobbyPhase : ComponentBase
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public CardCounterGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        protected async Task StartGame()
        {
            if (UserService.CurrentUser == null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start game: {Error}", error);
        }

        protected void NotifyConfigChanged()
        {
            GameState.StateChangedEventManager.Notify();
        }
    }
}
