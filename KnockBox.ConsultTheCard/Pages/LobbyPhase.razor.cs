using KnockBox.Core.Extensions.Collections;
using KnockBox.ConsultTheCard.Services.Logic.Games;
using KnockBox.ConsultTheCard.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.ConsultTheCard.Pages
{
    public partial class LobbyPhase : ComponentBase
    {
        [Inject] protected ConsultTheCardGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public ConsultTheCardGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        protected int SetupPhaseTimeoutSeconds
        {
            get => GameState.Config.SetupPhaseTimeoutMs / 1000;
            set { GameState.Config.SetupPhaseTimeoutMs = value * 1000; NotifyConfigChanged(); }
        }

        protected int CluePhaseTimeoutSeconds
        {
            get => GameState.Config.CluePhaseTimeoutMs / 1000;
            set { GameState.Config.CluePhaseTimeoutMs = value * 1000; NotifyConfigChanged(); }
        }

        protected int DiscussionPhaseTimeoutSeconds
        {
            get => GameState.Config.DiscussionPhaseTimeoutMs / 1000;
            set { GameState.Config.DiscussionPhaseTimeoutMs = value * 1000; NotifyConfigChanged(); }
        }

        protected int VotePhaseTimeoutSeconds
        {
            get => GameState.Config.VotePhaseTimeoutMs / 1000;
            set { GameState.Config.VotePhaseTimeoutMs = value * 1000; NotifyConfigChanged(); }
        }

        protected int RevealPhaseTimeoutSeconds
        {
            get => GameState.Config.RevealPhaseTimeoutMs / 1000;
            set { GameState.Config.RevealPhaseTimeoutMs = value * 1000; NotifyConfigChanged(); }
        }

        protected int InformantGuessTimeoutSeconds
        {
            get => GameState.Config.InformantGuessTimeoutMs / 1000;
            set { GameState.Config.InformantGuessTimeoutMs = value * 1000; NotifyConfigChanged(); }
        }

        protected void KickPlayer(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                Logger.LogWarning("Cannot kick player: user ID is null or empty.");
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

