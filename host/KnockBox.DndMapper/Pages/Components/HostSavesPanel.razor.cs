using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Library;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostSavesPanel : DisposableComponent
    {
        private const string DownloadModulePath = "/_content/KnockBox.DndMapper/js/dndMapperFileDownload.js";

        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;
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

        private string? _exportingSlotId;
        private IJSObjectReference? _downloadModule;

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

        private async Task ExportSlot(string slotId)
        {
            if (_exportingSlotId is not null) return;
            _exportingSlotId = slotId;
            StateHasChanged();
            try
            {
                var result = await Library.ExportSlotAsync(slotId, ComponentDetached);
                if (result.IsCanceled) return;
                if (result.TryGetFailure(out var err))
                {
                    if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                    return;
                }
                if (!result.TryGetSuccess(out var export)) return;

                // Trigger the download via a hidden anchor click. The blob is
                // disposed in the finally so its object URL is revoked once
                // the browser has started the download.
                try
                {
                    var url = await export.Blob.CreateObjectUrlAsync(ComponentDetached);
                    _downloadModule ??= await JS.InvokeAsync<IJSObjectReference>("import", ComponentDetached, DownloadModulePath);
                    var fileName = SanitizeForFilename(export.SlotName) + ".vtf";
                    await _downloadModule.InvokeVoidAsync("downloadObjectUrl", ComponentDetached, url, fileName);
                }
                finally
                {
                    await export.Blob.DisposeAsync();
                }
            }
            catch (OperationCanceledException) { /* component detached */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "DnD Mapper slot export threw.");
                if (Toasts is not null) await Toasts.Push("Export failed.", DndMapperToastTone.Danger);
            }
            finally
            {
                _exportingSlotId = null;
                StateHasChanged();
            }
        }

        private static string SanitizeForFilename(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "slot";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            var name = sb.ToString().Trim();
            return string.IsNullOrEmpty(name) ? "slot" : name;
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
            if (_downloadModule is not null)
            {
                // Fire-and-forget — the circuit may already be tearing down,
                // and the module's only purpose was the anchor-click helper.
                _ = _downloadModule.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
                _downloadModule = null;
            }
            base.Dispose();
        }
    }
}
