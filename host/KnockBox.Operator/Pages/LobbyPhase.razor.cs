using KnockBox.Tooling.Collections;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Operator.Pages
{
    public partial class LobbyPhase : ComponentBase
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        protected int SetupPhaseTimeoutSeconds
        {
            get => (int)GameState.Config.SetupPhaseTimeout.TotalSeconds;
            set { GameState.Config.SetupPhaseTimeout = TimeSpan.FromSeconds(value); NotifyConfigChanged(); }
        }

        protected int PlayPhaseTimeoutSeconds
        {
            get => (int)GameState.Config.PlayPhaseTimeout.TotalSeconds;
            set { GameState.Config.PlayPhaseTimeout = TimeSpan.FromSeconds(value); NotifyConfigChanged(); }
        }

        protected int ReactionPhaseTimeoutSeconds
        {
            get => (int)GameState.Config.ReactionPhaseTimeout.TotalSeconds;
            set { GameState.Config.ReactionPhaseTimeout = TimeSpan.FromSeconds(value); NotifyConfigChanged(); }
        }

        protected int DrawPhaseTimeoutSeconds
        {
            get => (int)GameState.Config.DrawPhaseTimeout.TotalSeconds;
            set { GameState.Config.DrawPhaseTimeout = TimeSpan.FromSeconds(value); NotifyConfigChanged(); }
        }

        protected bool TimersEnabled
        {
            get => GameState.Config.TimersEnabled;
            set { GameState.Config.TimersEnabled = value; NotifyConfigChanged(); }
        }

        protected bool EnableStacking
        {
            get => GameState.Config.EnableStacking;
            set { GameState.Config.EnableStacking = value; NotifyConfigChanged(); }
        }

        protected bool FlipWinCondition
        {
            get => GameState.Config.FlipWinCondition;
            set { GameState.Config.FlipWinCondition = value; NotifyConfigChanged(); }
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

