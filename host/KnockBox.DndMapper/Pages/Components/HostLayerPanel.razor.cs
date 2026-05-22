using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostLayerPanel : DisposableComponent
    {
        [Parameter, EditorRequired]
        public DndMapperGameState State { get; set; } = default!;

        [Parameter] public string RoomCode { get; set; } = string.Empty;
        [Parameter] public Guid? SelectedImageId { get; set; }
        [Parameter] public EventCallback<Guid?> SelectedImageIdChanged { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        // Host-only direct-blob URLs keyed by image id. Same pattern as
        // MapCanvas._localImageUrls — populated lazily from the library's
        // blob cache so the host's own thumbnails render via a same-browser
        // blob: URL instead of round-tripping bytes through SignalR via
        // /blob-share/{token}. Empty on player circuits (their blob cache is
        // empty); ResolveImageSrc falls back to the share URL there.
        private readonly Dictionary<Guid, string> _localImageUrls = new();

        // Inline-rename state for layer rows. Dbl-clicking a layer's name swaps
        // the label for an input bound to _renameLayerDraft; Enter / blur
        // commits, Escape cancels.
        private Guid? _renamingImageId;
        private string _renameLayerDraft = string.Empty;
        private ElementReference _renameInputRef;
        private bool _renameFocusPending;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(OnStateChanged);
            base.OnInitialized();
        }

        protected override async Task OnParametersSetAsync()
        {
            await RefreshLocalImageUrlsAsync().ConfigureAwait(false);
            await base.OnParametersSetAsync().ConfigureAwait(false);
        }

        // Mirror of MapCanvas.OnStateChanged's local-URL refresh path:
        // OnParametersSetAsync only fires on parent-pushed parameter changes,
        // so a state-driven re-render (new upload, reconnect republish) won't
        // pick up freshly-cached blob URLs without this hook.
        private async ValueTask OnStateChanged()
        {
            await RefreshLocalImageUrlsAsync().ConfigureAwait(false);
            await InvokeAsync(StateHasChanged);
        }

        // Shape mirrors MapCanvas.ResolveImageSrc. Host: hits _localImageUrls
        // → same-browser blob: URL. Player: _localImageUrls is empty so falls
        // through to the /blob-share/{token} capability URL.
        //
        // The Library.HasLocalBlob check is the crucial first-render guard:
        // if we own the blob but RefreshLocalImageUrlsAsync hasn't finished
        // its JS round-trip yet, return null instead of the share URL.
        // Falling through to /blob-share/ here would fire a SignalR-backed
        // fetch from the host's own browser to itself, contending with
        // session-attach traffic — the exact race that previously timed
        // out and tore the circuit down on session load.
        private string? ResolveImageSrc(MapImage img)
        {
            if (_localImageUrls.TryGetValue(img.Id, out var localUrl) && localUrl is not null)
                return localUrl;
            if (Library.HasLocalBlob(img.Id)) return null;
            if (img.ShareToken is Guid shareToken)
                return $"/blob-share/{shareToken:D}";
            return null;
        }

        // Same shape as MapCanvas.RefreshLocalImageUrlsAsync. Re-renders only
        // when the set actually changes — avoids a render storm during the
        // host's first attach (which republishes every image's share token in
        // parallel and would otherwise fire one render per image).
        private async ValueTask RefreshLocalImageUrlsAsync()
        {
            var map = ActiveMap;
            if (map is null) return;
            var changed = false;

            if (_localImageUrls.Count > 0)
            {
                var currentIds = new HashSet<Guid>(map.Images.Select(i => i.Id));
                foreach (var key in _localImageUrls.Keys.ToList())
                {
                    if (!currentIds.Contains(key))
                    {
                        _localImageUrls.Remove(key);
                        changed = true;
                    }
                }
            }

            foreach (var img in map.Images)
            {
                if (_localImageUrls.ContainsKey(img.Id)) continue;
                var url = await Library.TryGetLocalObjectUrlAsync(img.Id).ConfigureAwait(false);
                if (url is null) continue;
                _localImageUrls[img.Id] = url;
                changed = true;
            }

            if (changed) await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_renameFocusPending && _renamingImageId is not null)
            {
                _renameFocusPending = false;
                try { await _renameInputRef.FocusAsync(preventScroll: true); }
                catch { /* element not yet attached / circuit teardown */ }
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private Map? ActiveMap
        {
            get
            {
                if (State?.ActiveMapId is null) return null;
                return State.Maps.FirstOrDefault(map => map.Id == State.ActiveMapId);
            }
        }

        private async Task SelectImage(Guid id)
        {
            await SelectedImageIdChanged.InvokeAsync(id == SelectedImageId ? null : id);
        }

        // Hidden layers must not appear in the host's selection cycle — clicking
        // a hidden row is a no-op (the eye button is still active, so the host
        // can still un-hide it).
        private async Task OnRowClick(MapImage img)
        {
            if (img.Hidden) return;
            await SelectImage(img.Id);
        }

        private async Task ToggleLocked(MapImage img)
        {
            if (UserService.CurrentUser is null || State.ActiveMapId is not Guid mapId) return;
            var result = Engine.SetImageLockedAsync(State, UserService.CurrentUser, mapId, img.Id, !img.Locked);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private async Task ToggleHidden(MapImage img)
        {
            if (UserService.CurrentUser is null || State.ActiveMapId is not Guid mapId) return;
            var nextHidden = !img.Hidden;
            var result = Engine.SetImageHiddenAsync(State, UserService.CurrentUser, mapId, img.Id, nextHidden);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            // If we just hid the selected image, clear the selection so the
            // inspector dismisses.
            if (nextHidden && SelectedImageId == img.Id)
            {
                await SelectedImageIdChanged.InvokeAsync(null);
            }
        }

        private void BeginLayerRename(MapImage img)
        {
            _renamingImageId = img.Id;
            // Mirror the visible label so the rename input opens on the same
            // text the user sees (including the synthetic "Layer #N" fallback
            // when Name is blank).
            _renameLayerDraft = img.DisplayName;
            _renameFocusPending = true;
        }

        private void CancelLayerRename()
        {
            _renamingImageId = null;
            _renameLayerDraft = string.Empty;
        }

        private void OnLayerRenameInput(ChangeEventArgs e)
        {
            _renameLayerDraft = e.Value as string ?? string.Empty;
        }

        private async Task OnLayerRenameKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") await CommitLayerRename();
            else if (e.Key == "Escape") CancelLayerRename();
        }

        private async Task CommitLayerRename()
        {
            if (_renamingImageId is not Guid imageId)
            {
                CancelLayerRename();
                return;
            }
            if (UserService.CurrentUser is null || State.ActiveMapId is not Guid mapId)
            {
                CancelLayerRename();
                return;
            }
            var img = ActiveMap?.Images.FirstOrDefault(i => i.Id == imageId);
            if (img is null)
            {
                CancelLayerRename();
                return;
            }
            var trimmed = (_renameLayerDraft ?? string.Empty).Trim();
            // If the layer currently has no persisted name, the input opened on the
            // synthetic fallback (MapImage.DisplayName) — leaving that unchanged
            // should not persist the fallback as a real name.
            bool isUnchangedFallback = string.IsNullOrWhiteSpace(img.Name)
                && trimmed == img.DisplayName;
            if (!isUnchangedFallback && trimmed != (img.Name ?? string.Empty))
            {
                var result = Engine.SetImageNameAsync(State, UserService.CurrentUser, mapId, imageId, trimmed);
                if (result.TryGetFailure(out var err) && Toasts is not null)
                {
                    await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                }
            }
            CancelLayerRename();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
