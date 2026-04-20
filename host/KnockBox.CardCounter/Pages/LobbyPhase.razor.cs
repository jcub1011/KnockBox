using KnockBox.Tooling.Collections;
using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.CardCounter.Pages
{
    public partial class LobbyPhase : ComponentBase
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public CardCounterGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        protected void KickPlayer(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                Logger.LogWarning("Unable to kick provided user as it is null/whitespace.");
                return;
            }

            if (GameState.Host.Id != UserService.CurrentUser?.Id)
            {
                Logger.LogWarning("You [{id}] cannot kick players as you are not the host.", UserService.CurrentUser?.Id);
                return;
            }
            
            if (UserService.CurrentUser?.Id == userId)
            {
                Logger.LogWarning("Unable to kick host [{id}] from game.", userId);
                return;
            }

            int index = GameState.Players.IndexOf(user => user.Id == userId);
            if (index < 0)
            {
                Logger.LogWarning("Unable to kick player [{id}] as they aren't in the lobby.", userId);
                return;
            }

            var result = GameState.KickPlayer(GameState.Players[index]);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogWarning("Error kicking player [{error}].", error.PublicMessage);
            }
        }

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

