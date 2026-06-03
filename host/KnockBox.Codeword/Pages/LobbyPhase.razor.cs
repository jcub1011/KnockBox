using KnockBox.Tooling.Collections;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.AspNetCore.Components;

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

        // True once the host has changed any setting locally. Blocks the initial
        // localStorage load from clobbering an in-flight edit if the load returns
        // after the user has already interacted with the drawer.
        private bool _userHasEdited;

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

        // Delegates the atomic mutation to the state (which enforces Execute + reflects
        // HostPlays into HostIsParticipant) and then persists. _userHasEdited blocks
        // any in-flight localStorage load from clobbering this edit.
        private void UpdateSettings(Func<CodewordSettings, CodewordSettings> mutate)
        {
            _userHasEdited = true;
            if (GameState.UpdateSettings(mutate).TryGetFailure(out var error))
            {
                Logger.LogError("Failed to update Codeword settings: {Error}", error.PublicMessage);
                return;
            }
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
            _resetGeneration++;                          // invalidate any pending disarm
            UpdateSettings(_ => new CodewordSettings());
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

        // Two lobby buttons share this handler: "Start Game" (hostAsPlayer: false) runs the host
        // as a shared display; "Start Game As Player" (hostAsPlayer: true) deals the host in. The
        // choice is persisted through the same UpdateSettings path as every other setting, which
        // reflects HostPlays into HostIsParticipant before the engine snapshots participants.
        protected async Task StartGame(bool hostAsPlayer)
        {
            if (UserService.CurrentUser is null) return;
            UpdateSettings(s => s with { HostPlays = hostAsPlayer });
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
            var savedResult = await LocalStorage.GetAsync<CodewordSettings>("codeword", "settings", _cts.Token);
            // If the host already edited a setting while the load was in flight,
            // the user's edit wins — the saved snapshot would clobber it. A failed/canceled
            // read is a non-success result that simply falls through to built-in defaults.
            if (savedResult.TryGetSuccess(out var saved) && saved is not null && !_userHasEdited)
            {
                if (GameState.UpdateSettings(_ => saved).TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to apply saved Codeword settings: {Error}", error.PublicMessage);
                    return;
                }
                StateHasChanged();
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
            var saveResult = await LocalStorage.SetAsync("codeword", "settings", settings, ct);
            if (saveResult.TryGetFailure(out var saveError))
                Logger.LogError("Error saving Codeword settings: {Error}", saveError.InternalMessage);
        }

        public async ValueTask DisposeAsync()
        {
            // Flush the last pending save before tearing down so a change made right before
            // navigating away isn't lost. Storage calls no longer throw on a dead circuit —
            // they return a non-success Result, which SaveSettingsAsync logs and ignores.
            if (_saveTask is not null)
            {
                try { await _saveTask; } catch { /* best-effort flush */ }
            }

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
