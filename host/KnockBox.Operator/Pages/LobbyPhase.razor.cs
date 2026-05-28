using KnockBox.Tooling.Collections;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.Operator.Pages
{
    public partial class LobbyPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILocalStorageService LocalStorage { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        // True once the host has changed any setting locally. Blocks the initial localStorage
        // load from clobbering an in-flight edit if the load returns after the user interacted.
        private bool _userHasEdited;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        // Per-property setters route through UpdateSettings so every mutation runs inside
        // State.Execute (atomic + change notification) and gets persisted to localStorage.
        protected int SetupPhaseTimeoutSeconds
        {
            get => (int)GameState.Settings.SetupPhaseTimeout.TotalSeconds;
            set => UpdateSettings(s => s with { SetupPhaseTimeout = TimeSpan.FromSeconds(value) });
        }

        protected int PlayPhaseTimeoutSeconds
        {
            get => (int)GameState.Settings.PlayPhaseTimeout.TotalSeconds;
            set => UpdateSettings(s => s with { PlayPhaseTimeout = TimeSpan.FromSeconds(value) });
        }

        protected int ReactionPhaseTimeoutSeconds
        {
            get => (int)GameState.Settings.ReactionPhaseTimeout.TotalSeconds;
            set => UpdateSettings(s => s with { ReactionPhaseTimeout = TimeSpan.FromSeconds(value) });
        }

        protected int DrawPhaseTimeoutSeconds
        {
            get => (int)GameState.Settings.DrawPhaseTimeout.TotalSeconds;
            set => UpdateSettings(s => s with { DrawPhaseTimeout = TimeSpan.FromSeconds(value) });
        }

        protected bool TimersEnabled
        {
            get => GameState.Settings.TimersEnabled;
            set => UpdateSettings(s => s with { TimersEnabled = value });
        }

        protected bool EnableStacking
        {
            get => GameState.Settings.EnableStacking;
            set => UpdateSettings(s => s with { EnableStacking = value });
        }

        protected bool FlipWinCondition
        {
            get => GameState.Settings.FlipWinCondition;
            set => UpdateSettings(s => s with { FlipWinCondition = value });
        }

        private void UpdateSettings(Func<OperatorSettings, OperatorSettings> mutate)
        {
            _userHasEdited = true;
            GameState.UpdateSettings(mutate);
            PersistSettings();
        }

        // Two-step confirm so an accidental click can't wipe the host's whole config.
        // First click arms; a second click within the window resets. Auto-disarms after ~3s.
        private bool _resetArmed;
        private int _resetGeneration;

        protected void ResetToDefaults()
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                _ = DisarmResetAfterDelay(++_resetGeneration);
                return;
            }
            _resetArmed = false;
            _resetGeneration++;                            // invalidate any pending disarm
            UpdateSettings(_ => new OperatorSettings());
        }

        private async Task DisarmResetAfterDelay(int generation)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
            catch (OperationCanceledException) { return; }   // component disposed
            if (generation != _resetGeneration) return;      // superseded by a newer arm/reset
            _resetArmed = false;
            await InvokeAsync(StateHasChanged);
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

            int index = GameState.Players.IndexOf(entry => entry.User.Id == userId);
            if (index < 0)
            {
                Logger.LogWarning("Unable to kick player [{id}] as they aren't in the lobby.", userId);
                return;
            }

            var result = GameState.KickPlayer(UserService.CurrentUser!, GameState.Players[index].User);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogWarning("Error kicking player [{error}].", error.PublicMessage);
            }
        }

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start game: {Error}", error);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // localStorage needs JS interop, so the host's saved settings load here (not
            // OnInitialized, which also runs during prerender). Host-only — only the host
            // edits and persists these.
            if (firstRender && UserService.CurrentUser?.Id == GameState.Host.Id)
            {
                await LoadSettingsAsync();
            }
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                var saved = await LocalStorage.GetAsync<OperatorSettings>("operator", "settings", _cts.Token);
                // If the host already edited a setting while the load was in flight,
                // the user's edit wins — the saved snapshot would clobber it.
                if (saved is not null && !_userHasEdited)
                {
                    GameState.UpdateSettings(_ => saved);
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading Operator settings.");
            }
        }

        private void PersistSettings()
        {
            var snapshot = GameState.Settings;
            _saveTask = SaveSettingsAsync(snapshot, _saveTask, _cts.Token);
        }

        private async Task SaveSettingsAsync(OperatorSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            try
            {
                await LocalStorage.SetAsync("operator", "settings", settings, ct);
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving Operator settings.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Flush the last pending save before tearing down so a change made right before
            // navigating away isn't lost. A dead circuit makes SetAsync throw
            // JSDisconnectedException, which SaveSettingsAsync swallows.
            if (_saveTask is not null)
            {
                try { await _saveTask; } catch { /* best-effort flush */ }
            }

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
