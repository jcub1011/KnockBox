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
    public partial class HostMapSwitcher : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        private Guid? _renamingId;
        private string _renameDraft = string.Empty;
        private ElementReference _renameInputRef;
        private bool _focusRenameOnRender;

        private int? _dragSourceIndex;
        private int? _dragOverIndex;

        private Map? _pendingDelete;
        private Map? _settingsTarget;

        private List<Map> Maps =>
            [.. State.Maps.OrderBy(m => m.ListOrder).ThenBy(m => m.CreatedUtc)];

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_focusRenameOnRender)
            {
                _focusRenameOnRender = false;
                try { await _renameInputRef.FocusAsync(); }
                catch { /* element may not be present */ }
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private async Task OnNewMap()
        {
            if (UserService.CurrentUser is null) return;
            var name = $"Map {State.Maps.Length + 1}";
            var result = Engine.CreateMapAsync(State, UserService.CurrentUser, name);
            if (result.TryGetSuccess(out var newId))
            {
                var activate = Engine.SetActiveMapAsync(State, UserService.CurrentUser, newId);
                if (activate.TryGetFailure(out var activateErr))
                {
                    await PushToast(activateErr.PublicMessage, DndMapperToastTone.Danger);
                }
            }
            else if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void OnRowClick(Guid mapId)
        {
            if (_renamingId is not null) return;
            if (UserService.CurrentUser is null) return;
            Engine.SetActiveMapAsync(State, UserService.CurrentUser, mapId);
        }

        private void StartRename(Map m)
        {
            _renamingId = m.Id;
            _renameDraft = m.Name;
            _focusRenameOnRender = true;
        }

        private async Task CommitRename(Guid mapId)
        {
            if (_renamingId != mapId) return;
            var name = (_renameDraft ?? string.Empty).Trim();
            _renamingId = null;
            if (UserService.CurrentUser is null || string.IsNullOrEmpty(name)) return;
            var result = Engine.RenameMapAsync(State, UserService.CurrentUser, mapId, name);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnRenameKey(KeyboardEventArgs e, Guid mapId)
        {
            if (e.Key == "Enter") await CommitRename(mapId);
            else if (e.Key == "Escape") { _renamingId = null; }
        }

        private async Task OnDuplicate(Guid mapId)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.DuplicateMapAsync(State, UserService.CurrentUser, mapId);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void OnDeleteRequest(Map m) => _pendingDelete = m;

        private void OpenSettings(Map m) => _settingsTarget = m;

        private void CloseSettings() => _settingsTarget = null;

        private async Task OnSettingsSaved(GridConfig grid)
        {
            var target = _settingsTarget;
            if (target is null || UserService.CurrentUser is null) return;

            var result = Engine.UpdateGridAsync(State, UserService.CurrentUser, target.Id, grid);
            if (result.TryGetFailure(out var err))
            {
                // Keep the modal open so the user can correct values; surface the
                // error via toast since the modal's own _error path is limited to
                // local validation failures.
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            _settingsTarget = null;
        }

        private void CancelDelete() => _pendingDelete = null;

        private async Task ConfirmDelete()
        {
            var pending = _pendingDelete;
            _pendingDelete = null;
            if (pending is null || UserService.CurrentUser is null) return;
            // Route through the library service so the deleted map's image blob
            // shares get revoked and IndexedDB rows cleared — the engine verb
            // only mutates in-memory state.
            var result = await Library.DeleteMapAsync(State, UserService.CurrentUser, pending.Id);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void OnDragStart(int sourceIndex) => _dragSourceIndex = sourceIndex;

        private void OnDragOver(int overIndex) => _dragOverIndex = overIndex;

        private async Task OnDrop(int targetIndex)
        {
            var source = _dragSourceIndex;
            _dragSourceIndex = null;
            _dragOverIndex = null;
            if (source is null || source == targetIndex) return;
            if (UserService.CurrentUser is null) return;

            var ordered = Maps.Select(m => m.Id).ToList();
            var moved = ordered[source.Value];
            ordered.RemoveAt(source.Value);
            ordered.Insert(targetIndex, moved);

            var result = Engine.ReorderMapsAsync(State, UserService.CurrentUser, ordered);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private Task PushToast(string message, DndMapperToastTone tone)
            => Toasts is null ? Task.CompletedTask : Toasts.Push(message, tone);

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
