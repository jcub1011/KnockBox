using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
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
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        // Inline-rename state for layer rows. Dbl-clicking a layer's name swaps
        // the label for an input bound to _renameLayerDraft; Enter / blur
        // commits, Escape cancels.
        private Guid? _renamingImageId;
        private string _renameLayerDraft = string.Empty;
        private ElementReference _renameInputRef;
        private bool _renameFocusPending;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
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
