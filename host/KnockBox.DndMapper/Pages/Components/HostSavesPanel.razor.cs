using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Library;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostSavesPanel : DisposableComponent
    {
        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected ILogger<HostSavesPanel> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private List<SlotInfo> _slots = new();
        private string? _error;

        private bool _creating;
        private string _newName = string.Empty;

        private string? _renamingId;
        private string _renameDraft = string.Empty;

        private string? _pendingDelete;
        private string _pendingDeleteName = string.Empty;
        private string? _pendingOverwrite;
        private string _pendingOverwriteName = string.Empty;
        private string? _pendingLoad;
        private string _pendingLoadName = string.Empty;

        protected override async Task OnInitializedAsync()
        {
            Library.SlotsChanged += OnSlotsChanged;
            Library.SavingChanged += OnSavingChanged;
            await RefreshAsync();
            await base.OnInitializedAsync();
        }

        private void OnSlotsChanged() => _ = InvokeAsync(RefreshAsync);
        // Auto Save updates its timestamp on every flush; refresh so the
        // "Updated <when>" stays current.
        private void OnSavingChanged() => _ = InvokeAsync(RefreshAsync);

        private async Task RefreshAsync()
        {
            var result = await Library.ListSlotsAsync(ComponentDetached);
            if (result.TryGetSuccess(out var slots))
            {
                _slots = slots.ToList();
                _error = null;
            }
            else
            {
                result.TryGetFailure(out var err);
                _error = err.PublicMessage;
            }
            StateHasChanged();
        }

        private void OpenCreate()
        {
            _newName = string.Empty;
            _creating = true;
        }

        private void CancelCreate() => _creating = false;

        private async Task ConfirmCreate()
        {
            var result = await Library.CreateSlotAsync(_newName, ComponentDetached);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            _creating = false;
            _newName = string.Empty;
        }

        private void BeginRename(SlotInfo s)
        {
            _renamingId = s.Id;
            _renameDraft = s.Name;
        }

        private void CancelRename()
        {
            _renamingId = null;
            _renameDraft = string.Empty;
        }

        private async Task ConfirmRename(string slotId)
        {
            var result = await Library.RenameSlotAsync(slotId, _renameDraft, ComponentDetached);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            _renamingId = null;
            _renameDraft = string.Empty;
        }

        private void RequestOverwrite(string id)
        {
            _pendingOverwrite = id;
            _pendingOverwriteName = _slots.FirstOrDefault(s => s.Id == id)?.Name ?? id;
        }

        private async Task ConfirmOverwrite()
        {
            var id = _pendingOverwrite;
            _pendingOverwrite = null;
            _pendingOverwriteName = string.Empty;
            if (id is null) return;
            var result = await Library.SaveToSlotAsync(id, ComponentDetached);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private void RequestDelete(string id)
        {
            _pendingDelete = id;
            _pendingDeleteName = _slots.FirstOrDefault(s => s.Id == id)?.Name ?? id;
        }

        private async Task ConfirmDelete()
        {
            var id = _pendingDelete;
            _pendingDelete = null;
            _pendingDeleteName = string.Empty;
            if (id is null) return;
            var result = await Library.DeleteSlotAsync(id, ComponentDetached);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private void RequestLoad(string id)
        {
            _pendingLoad = id;
            _pendingLoadName = _slots.FirstOrDefault(s => s.Id == id)?.Name ?? id;
        }

        private async Task ConfirmLoad()
        {
            var id = _pendingLoad;
            _pendingLoad = null;
            _pendingLoadName = string.Empty;
            if (id is null) return;
            var result = await Library.LoadSlotAsync(id, ComponentDetached);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private static string FormatRelative(DateTime utc)
        {
            var delta = DateTime.UtcNow - utc;
            if (delta.TotalSeconds < 5) return "just now";
            if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        public override void Dispose()
        {
            Library.SlotsChanged -= OnSlotsChanged;
            Library.SavingChanged -= OnSavingChanged;
            base.Dispose();
        }
    }
}
