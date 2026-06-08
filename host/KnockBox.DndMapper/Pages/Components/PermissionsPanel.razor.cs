using KnockBox.Core.Components.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Storage;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class PermissionsPanel : DisposableComponent, IAsyncDisposable
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public bool Embedded { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected DndMapperStorage Storage { get; set; } = default!;
        [Inject] protected ILogger<PermissionsPanel> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        // True once the host has changed any setting locally. Blocks the initial localStorage
        // load from clobbering an in-flight edit if the load returns after the user interacted.
        private bool _userHasEdited;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // localStorage needs JS interop, so the host's saved settings load here (not
            // OnInitialized, which also runs during prerender). Host-only — only the host
            // edits and persists these.
            if (firstRender && UserService.CurrentUser?.Id == State.Host.Id)
            {
                await LoadSettingsAsync();
            }
        }

        private static string PillCls(bool active) => active ? "active" : string.Empty;

        private Task SetTokenMovement(TokenMovementPolicy v)
            => Apply(State.Settings with { TokenMovement = v });

        private Task SetSheetEdit(SheetEditPolicy v)
            => Apply(State.Settings with { SheetEditByOthers = v });

        private Task SetRollsVisible(bool v)
            => Apply(State.Settings with { RollsVisibleToPlayers = v });

        private Task SetPlayersCanCreateNpcs(bool v)
            => Apply(State.Settings with { PlayersCanCreateNPCs = v });

        private Task SetPlayersCanSeeOtherSheets(bool v)
            => Apply(State.Settings with { PlayersCanSeeOtherSheets = v });

        private Task SetLoadedDiceEnabled(bool v)
            => Apply(State.Settings with { LoadedDiceEnabled = v });

        private Task SetLoadedDiceVisibility(LoadedDiceRuleVisibility v)
            => Apply(State.Settings with { LoadedDiceRuleVisibility = v });

        private Task SetLoadedDicePlayerIndicator(LoadedDicePlayerIndicator v)
            => Apply(State.Settings with { LoadedDicePlayerIndicator = v });

        private async Task Apply(DndMapperSettings next)
        {
            if (UserService.CurrentUser is null) return;
            _userHasEdited = true;
            var result = Engine.UpdateSettingsAsync(State, UserService.CurrentUser, next);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            PersistSettings();
        }

        // Two-step confirm so an accidental click can't wipe the host's whole config.
        // First click arms; a second click within the window resets. Auto-disarms after ~3s.
        // Apply() is host-checked by the engine and persists to both localStorage and the
        // IndexedDB session snapshot, so the reset propagates exactly like any other edit.
        private bool _resetArmed;
        private int _resetGeneration;

        private async Task ResetToDefaults()
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                _ = DisarmResetAfterDelay(++_resetGeneration);
                return;
            }
            _resetArmed = false;
            _resetGeneration++;                          // invalidate any pending disarm
            await Apply(new DndMapperSettings());
        }

        private async Task DisarmResetAfterDelay(int generation)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
            catch (OperationCanceledException) { return; }   // component disposed
            if (generation != _resetGeneration) return;      // superseded by a newer arm/reset
            _resetArmed = false;
            await InvokeAsync(StateHasChanged);
        }

        private async Task LoadSettingsAsync()
        {
            // A failed/canceled read falls through to the built-in defaults already on the state.
            var savedResult = await Storage.Local.GetAsync<DndMapperSettings>("settings", "value", _cts.Token);
            // If the host already edited a setting while the load was in flight,
            // the user's edit wins — the saved snapshot would clobber it.
            if (savedResult.TryGetSuccess(out var saved) && saved is not null && !_userHasEdited)
            {
                if (State.UpdateSettings(_ => saved).TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to apply saved DndMapper settings: {Error}", error.PublicMessage);
                    return;
                }
                StateHasChanged();
            }
        }

        private void PersistSettings()
        {
            var snapshot = State.Settings;
            _saveTask = SaveSettingsAsync(snapshot, _saveTask, _cts.Token);
        }

        private async Task SaveSettingsAsync(DndMapperSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            var saveResult = await Storage.Local.SetAsync("settings", "value", settings, ct);
            if (saveResult.TryGetFailure(out var saveError))
                Logger.LogError("Error saving Dnd Mapper settings: {Error}", saveError.InternalMessage);
        }

        private Task PushToast(string message, DndMapperToastTone tone)
            => Toasts is null ? Task.CompletedTask : Toasts.Push(message, tone);

        public async ValueTask DisposeAsync()
        {
            // Flush the last pending save before tearing down so a change made right before
            // navigating away isn't lost. A dead circuit makes SetAsync return a failed Result,
            // which SaveSettingsAsync logs and swallows.
            if (_saveTask is not null)
            {
                try { await _saveTask; } catch { /* best-effort flush */ }
            }

            _cts.Cancel();
            _cts.Dispose();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
