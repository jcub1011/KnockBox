using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class LobbyPhase : ComponentBase, IAsyncDisposable
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameEngine Engine { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;
        [Parameter, EditorRequired] public string RoomCode { get; set; } = default!;

        [Inject] protected ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        private bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        // True once the host has changed any setting locally. Blocks the initial localStorage
        // load from clobbering an in-flight edit if the load returns after the user interacted.
        private bool _userHasEdited;

        protected void SetTotalRounds(int value) => UpdateSettings(s => s with { TotalRounds = value });
        protected void SetEnableTimers(bool value) => UpdateSettings(s => s with { EnableTimers = value });
        protected void SetPoolRotation(TaskPoolRotation value) => UpdateSettings(s => s with { PoolRotation = value });

        // Delegates the atomic mutation to the state (which enforces Execute + change
        // notification) and then persists. _userHasEdited blocks any in-flight localStorage
        // load from clobbering this edit.
        private void UpdateSettings(Func<HiddenAgendaSettings, HiddenAgendaSettings> mutate)
        {
            _userHasEdited = true;
            if (GameState.UpdateSettings(mutate).TryGetFailure(out var error))
            {
                Logger.LogError("Failed to update Hidden Agenda settings: {Error}", error.PublicMessage);
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
            _resetGeneration++;                              // invalidate any pending disarm
            UpdateSettings(_ => new HiddenAgendaSettings());
        }

        private async Task DisarmResetAfterDelay(int generation)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
            catch (OperationCanceledException) { return; }   // component disposed
            if (generation != _resetGeneration) return;      // superseded by a newer arm/reset
            _resetArmed = false;
            await InvokeAsync(StateHasChanged);
        }

        private async Task KickPlayer(User player)
        {
            if (UserService.CurrentUser is not { } caller) return;
            await GameState.ExecuteAsync(() =>
            {
                GameState.KickPlayer(caller, player);
                return ValueTask.CompletedTask;
            });
        }

        private async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            await Engine.StartAsync(UserService.CurrentUser, GameState);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // localStorage needs JS interop, so the host's saved settings load here (not
            // OnInitialized, which also runs during prerender). Host-only — only the host
            // edits and persists these.
            if (firstRender && IsHost)
            {
                await LoadSettingsAsync();
            }
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                var saved = await LocalStorage.GetAsync<HiddenAgendaSettings>("hidden-agenda", "settings", _cts.Token);
                // If the host already edited a setting while the load was in flight,
                // the user's edit wins — the saved snapshot would clobber it.
                if (saved is not null && !_userHasEdited)
                {
                    if (GameState.UpdateSettings(_ => saved).TryGetFailure(out var error))
                    {
                        Logger.LogError("Failed to apply saved Hidden Agenda settings: {Error}", error.PublicMessage);
                        return;
                    }
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading Hidden Agenda settings.");
            }
        }

        private void PersistSettings()
        {
            var snapshot = GameState.Settings;
            _saveTask = SaveSettingsAsync(snapshot, _saveTask, _cts.Token);
        }

        private async Task SaveSettingsAsync(HiddenAgendaSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            try
            {
                await LocalStorage.SetAsync("hidden-agenda", "settings", settings, ct);
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving Hidden Agenda settings.");
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
