using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Pages.Components;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages
{
    public partial class DndMapperPlayingPhase : DisposableComponent, IAsyncDisposable
    {
        private const int MinRailPx = 200;
        private const int MaxRailPx = 600;
        private const int DefaultLeftPx = 280;
        private const int DefaultRightPx = 320;

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string RoomCode { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;

        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] protected ILogger<DndMapperPlayingPhase> Logger { get; set; } = default!;
        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected NavigationManager NavigationManager { get; set; } = default!;

        private bool _libraryBannerVisible;
        private bool _hydrating;
        private string? _hydrateError;

        private readonly DndMapperToastService _toasts = new();
        private readonly DiceRollerConfig _diceConfig = new();
        private readonly DndMapperViewport _viewport = new();

        private Guid? _selectedImageId;
        private bool _permsOpen;
        private bool _leftCollapsed;
        private bool _rightCollapsed;

        private int _leftRailPx = DefaultLeftPx;
        private int _rightRailPx = DefaultRightPx;

        private ElementReference _leftHandleRef;
        private ElementReference _rightHandleRef;
        private ElementReference _rootRef;

        private IJSObjectReference? _resizeModule;
        private IJSObjectReference? _unloadGuardModule;
        private IJSObjectReference? _collapseModule;
        private IJSObjectReference? _clipboardModule;
        private DotNetObjectReference<DndMapperPlayingPhase>? _dotNetRef;
        private bool _resizeAttached;
        private bool _isSaving;
        private bool _unloadGuardActive;

        private Map? ActiveMap =>
            State.ActiveMapId is Guid id
                ? State.Maps.FirstOrDefault(m => m.Id == id)
                : null;

        private string RootStyle =>
            string.Create(CultureInfo.InvariantCulture,
                $"--dndm-rail-w-left: {_leftRailPx}px; --dndm-rail-w-right: {_rightRailPx}px");

        private string Role => IsHost ? "host" : "player";

        private void OnSelectedImageIdChanged(Guid? id) => _selectedImageId = id;

        private void TogglePerms() => _permsOpen = !_permsOpen;

        private async Task OnOpenDisplayClicked()
        {
            if (!IsHost) return;
            var url = NavigationManager.ToAbsoluteUri($"room/dnd-mapper/{RoomCode}/display").ToString();

            try
            {
                _clipboardModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/KnockBox.DndMapper/js/dndMapperClipboard.js");
            }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to import clipboard module.");
            }

            var copied = false;
            if (_clipboardModule is not null)
            {
                try
                {
                    await _clipboardModule.InvokeVoidAsync("copy", url);
                    copied = true;
                }
                catch (JSDisconnectedException) { /* circuit teardown */ return; }
                catch (Exception)
                {
                    // Clipboard API can fail in insecure contexts or when the
                    // browser denies permission. The new tab still opens, so
                    // this is a soft failure — fall through to the open call.
                }

                try
                {
                    await _clipboardModule.InvokeVoidAsync("openTab", url);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to open display view tab.");
                }
            }

            await _toasts.Push(
                copied ? "Display link copied to clipboard." : "Opening display view…",
                DndMapperToastTone.Info);
        }

        private void ToggleLeftRail() => _leftCollapsed = !_leftCollapsed;
        private void ToggleRightRail() => _rightCollapsed = !_rightCollapsed;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            if (!IsHost || UserService.CurrentUser is null) return;

            // Host-only: open the per-browser library and probe whether a
            // previous-session snapshot exists. The banner is rendered when
            // it does; the user picks Load or Start fresh.
            var attach = await Library.AttachAsync(State, UserService.CurrentUser, ComponentDetached);
            if (attach.TryGetFailure(out var err))
            {
                Logger.LogWarning("Failed to attach DnD Mapper library on host page: {Error}", err.PublicMessage);
                return;
            }
            _libraryBannerVisible = Library.HasExistingLibrary;
            Library.SavingChanged += OnSavingChanged;
        }

        private void OnSavingChanged()
        {
            _isSaving = Library.IsSaving;
            _ = InvokeAsync(async () =>
            {
                try
                {
                    StateHasChanged();
                    if (_unloadGuardModule is null) return;
                    if (_isSaving && !_unloadGuardActive)
                    {
                        await _unloadGuardModule.InvokeVoidAsync("enable");
                        _unloadGuardActive = true;
                    }
                    else if (!_isSaving && _unloadGuardActive)
                    {
                        await _unloadGuardModule.InvokeVoidAsync("disable");
                        _unloadGuardActive = false;
                    }
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (ObjectDisposedException) { /* component disposed */ }
                catch (Exception ex) { Logger.LogWarning(ex, "Failed to toggle unload guard."); }
            });
        }

        private async Task OnLoadLibrary()
        {
            if (_hydrating || !IsHost) return;
            // Set the guard before the first await so a rapid second click
            // can't slip past while the disabled binding is still rendering.
            _hydrating = true;
            _hydrateError = null;
            StateHasChanged();
            try
            {
                var result = await Library.HydrateAsync(ComponentDetached);
                if (result.TryGetFailure(out var err))
                {
                    _hydrateError = err.PublicMessage;
                    Logger.LogWarning("DnD Mapper hydration failed: {Error}", err.PublicMessage);
                    return;
                }
                _libraryBannerVisible = false;
            }
            finally
            {
                _hydrating = false;
                StateHasChanged();
            }
        }

        private async Task OnDiscardLibrary()
        {
            if (_hydrating || !IsHost) return;
            // Set the guard before the first await; see OnLoadLibrary.
            _hydrating = true;
            _hydrateError = null;
            StateHasChanged();
            try
            {
                var result = await Library.DiscardLibraryAsync(ComponentDetached);
                if (result.TryGetFailure(out var err))
                {
                    _hydrateError = err.PublicMessage;
                    Logger.LogWarning("DnD Mapper discard failed: {Error}", err.PublicMessage);
                    return;
                }
                _libraryBannerVisible = false;
            }
            finally
            {
                _hydrating = false;
                StateHasChanged();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    _resizeModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./_content/KnockBox.DndMapper/js/dndMapperRailResize.js");
                    _collapseModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./_content/KnockBox.DndMapper/js/dndMapperPanelCollapse.js");
                    await _collapseModule.InvokeVoidAsync("ensureInstalled");
                    if (IsHost)
                    {
                        _unloadGuardModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                            "import", "./_content/KnockBox.DndMapper/js/dndMapperUnloadGuard.js");
                    }

                    _rightRailPx = await _resizeModule.InvokeAsync<int>("load", "right", Role, DefaultRightPx);
                    if (IsHost)
                    {
                        _leftRailPx = await _resizeModule.InvokeAsync<int>("load", "left", "host", DefaultLeftPx);
                    }
                    _dotNetRef = DotNetObjectReference.Create(this);
                    StateHasChanged();
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to initialize rail resize JS.");
                }
            }
            else if (_resizeModule is not null && !_resizeAttached && _dotNetRef is not null)
            {
                try
                {
                    await _resizeModule.InvokeVoidAsync("attach", _rightHandleRef, "right", _dotNetRef, _rootRef);
                    if (IsHost)
                    {
                        await _resizeModule.InvokeVoidAsync("attach", _leftHandleRef, "left", _dotNetRef, _rootRef);
                    }
                    _resizeAttached = true;
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to attach rail resize handles.");
                }
            }

            await base.OnAfterRenderAsync(firstRender);
        }

        [JSInvokable]
        public async Task OnRailToggleCollapse(string side)
        {
            if (side == "left") _leftCollapsed = !_leftCollapsed;
            else if (side == "right") _rightCollapsed = !_rightCollapsed;
            else return;
            await InvokeAsync(StateHasChanged);
        }

        [JSInvokable]
        public async Task OnRailResize(string side, int px, bool persist)
        {
            px = Math.Clamp(px, MinRailPx, MaxRailPx);
            if (side == "left") _leftRailPx = px;
            else if (side == "right") _rightRailPx = px;
            else return;

            await InvokeAsync(StateHasChanged);

            // Only persist on pointer release (`persist=true`). Throttled
            // pointermove updates pass `persist=false` so we don't hit
            // localStorage dozens of times per second.
            if (persist && _resizeModule is not null)
            {
                try { await _resizeModule.InvokeVoidAsync("save", side, Role, px); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Cancel ComponentDetached first so any awaits on Library calls
            // (Attach, Hydrate, Discard) that are still in flight abort
            // before we start tearing down their target.
            base.Dispose();

            // Detach the library first so player UIs get placeholders via
            // ClearAllImageShareTokensAsync before the circuit's JS handle
            // is dropped. DetachAsync leaves the (DI-scoped) service alive
            // and re-attachable, so a host who leaves this room and joins
            // another in the same circuit gets a clean re-Attach. Final
            // disposal happens when the circuit ends and DI tears down the
            // scope.
            if (IsHost)
            {
                Library.SavingChanged -= OnSavingChanged;
                try { await Library.DetachAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception ex) { Logger.LogWarning(ex, "Failed to detach DnD Mapper library on page teardown."); }
            }

            if (_unloadGuardModule is not null)
            {
                try
                {
                    if (_unloadGuardActive) await _unloadGuardModule.InvokeVoidAsync("disable");
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
                try { await _unloadGuardModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
            }

            if (_resizeModule is not null)
            {
                try
                {
                    await _resizeModule.InvokeVoidAsync("detach", _rightHandleRef);
                    if (IsHost) await _resizeModule.InvokeVoidAsync("detach", _leftHandleRef);
                }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }

                try { await _resizeModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
            }
            if (_collapseModule is not null)
            {
                try { await _collapseModule.InvokeVoidAsync("dispose"); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
                try { await _collapseModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
            }
            if (_clipboardModule is not null)
            {
                try { await _clipboardModule.DisposeAsync(); }
                catch (JSDisconnectedException) { /* circuit teardown */ }
                catch (Exception) { /* ignore */ }
            }
            _dotNetRef?.Dispose();
        }
    }
}
