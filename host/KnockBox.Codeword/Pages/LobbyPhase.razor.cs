using KnockBox.Tooling.Collections;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.Codeword.Pages
{
    public partial class LobbyPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected CodewordGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILocalStorageService LocalStorage { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public CodewordGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        // Per-property setters route through UpdateSettings so every mutation runs inside
        // State.Execute (atomic + change notification) and gets persisted to localStorage.
        protected int SetupPhaseTimeoutSeconds
        {
            get => GameState.Settings.SetupPhaseTimeoutMs / 1000;
            set => UpdateSettings(s => s with { SetupPhaseTimeoutMs = value * 1000 });
        }

        protected int CluePhaseTimeoutSeconds
        {
            get => GameState.Settings.CluePhaseTimeoutMs / 1000;
            set => UpdateSettings(s => s with { CluePhaseTimeoutMs = value * 1000 });
        }

        protected int DiscussionPhaseTimeoutSeconds
        {
            get => GameState.Settings.DiscussionPhaseTimeoutMs / 1000;
            set => UpdateSettings(s => s with { DiscussionPhaseTimeoutMs = value * 1000 });
        }

        protected int VotePhaseTimeoutSeconds
        {
            get => GameState.Settings.VotePhaseTimeoutMs / 1000;
            set => UpdateSettings(s => s with { VotePhaseTimeoutMs = value * 1000 });
        }

        protected int RevealPhaseTimeoutSeconds
        {
            get => GameState.Settings.RevealPhaseTimeoutMs / 1000;
            set => UpdateSettings(s => s with { RevealPhaseTimeoutMs = value * 1000 });
        }

        protected int InformantGuessTimeoutSeconds
        {
            get => GameState.Settings.InformantGuessTimeoutMs / 1000;
            set => UpdateSettings(s => s with { InformantGuessTimeoutMs = value * 1000 });
        }

        protected void SetEnableTimers(bool value) => UpdateSettings(s => s with { EnableTimers = value });
        protected void SetTotalGames(int value) => UpdateSettings(s => s with { TotalGames = value });
        protected void SetHostPlaysGame(bool value) => UpdateSettings(s => s with { HostPlaysGame = value });

        // Replaces the Settings record inside State.Execute so readers on other circuits
        // never see a torn value and the change notification fires after the lock is
        // released. ApplyHostParticipation reflects the new HostPlaysGame value into the
        // shared Participants snapshot in the same critical section.
        private void UpdateSettings(Func<CodewordSettings, CodewordSettings> mutate)
        {
            GameState.Execute(() =>
            {
                GameState.Settings = mutate(GameState.Settings);
                GameState.ApplyHostParticipation();
            });
            PersistSettings();
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
                var saved = await LocalStorage.GetAsync<CodewordSettings>("codeword", "settings", _cts.Token);
                if (saved is not null)
                {
                    GameState.Execute(() =>
                    {
                        GameState.Settings = saved;
                        GameState.ApplyHostParticipation();
                    });
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading Codeword settings.");
            }
        }

        // Snapshot the current settings and chain this save off the previous one. Chaining
        // serializes the localStorage writes so rapid edits can't complete out of order and
        // leave a stale snapshot as the last writer.
        private void PersistSettings()
        {
            var snapshot = GameState.Settings;
            _saveTask = SaveSettingsAsync(snapshot, _saveTask, _cts.Token);
        }

        private async Task SaveSettingsAsync(CodewordSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            try
            {
                await LocalStorage.SetAsync("codeword", "settings", settings, ct);
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving Codeword settings.");
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
