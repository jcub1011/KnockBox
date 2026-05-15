using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class ImageUploadButton : DisposableComponent
    {
        private const long MaxImageBytes = 5L * 1024 * 1024;
        private const int MaxBatchFiles = 20;
        // Mirror of DndMapperGameEngine.SniffHeadLength: large enough to MIME-sniff
        // the magic bytes and locate JPEG SOF markers past EXIF metadata.
        private const int SniffHeadLength = 64 * 1024;

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public bool Compact { get; set; }

        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<ImageUploadButton> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _uploading;
        private int _batchDone;
        private int _batchTotal;
        private bool _libraryAttached;

        private bool _disabled => _uploading || State.ActiveMapId is null;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private async Task OnFileSelected(InputFileChangeEventArgs e)
        {
            if (UserService.CurrentUser is null) return;
            if (State.ActiveMapId is not Guid mapId) return;

            IReadOnlyList<IBrowserFile> files;
            try { files = e.GetMultipleFiles(MaxBatchFiles); }
            catch (InvalidOperationException)
            {
                await PushToast($"Too many files — upload up to {MaxBatchFiles} at a time.", DndMapperToastTone.Warning);
                return;
            }
            if (files.Count == 0) return;

            _uploading = true;
            _batchDone = 0;
            _batchTotal = files.Count;
            StateHasChanged();

            int successes = 0;
            var failures = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    if (ComponentDetached.IsCancellationRequested) break;
                    var error = await UploadSingleAsync(file, mapId);
                    if (error is null)
                    {
                        successes++;
                    }
                    else
                    {
                        var safeName = UploadBatchSummary.TruncateFilename(file.Name);
                        var detail = $"{safeName}: {error}";
                        Logger.LogWarning("Image upload failed: {Detail}", detail);
                        failures.Add(detail);
                    }
                    _batchDone++;
                    StateHasChanged();
                }
            }
            finally
            {
                _uploading = false;
                _batchDone = 0;
                _batchTotal = 0;
                StateHasChanged();
            }

            var summary = UploadBatchSummary.Build(successes, failures);
            await PushToast(summary.Message, summary.Tone);
        }

        private async Task<string?> UploadSingleAsync(IBrowserFile file, Guid mapId)
        {
            if (file.Size > MaxImageBytes) return "exceeds 5 MB";
            if (UserService.CurrentUser is null) return "no user";

            // Lazy-attach so smoke testing works without the parent page wiring
            // hydration. AttachAsync is idempotent — the parent page's call in a
            // later task short-circuits.
            if (!_libraryAttached)
            {
                var attach = await Library.AttachAsync(State, UserService.CurrentUser, ComponentDetached);
                if (attach.TryGetFailure(out var attachErr))
                    return attachErr.PublicMessage;
                if (attach.IsCanceled) return "canceled";
                _libraryAttached = true;
            }

            // Look up the active map's CellPixels under a read lock so we can
            // convert sniffed pixel dimensions into cell units (matching the
            // pre-migration default-size behavior in SaveImageAsync).
            int cellPixels = 1;
            string? capturedError = null;
            State.WithExclusiveRead(() =>
            {
                var map = State.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { capturedError = "map not found"; return; }
                cellPixels = Math.Max(1, map.Grid.CellPixels);
            });
            if (capturedError is not null) return capturedError;

            // Buffer the file into a MemoryStream so we can sniff the head AND
            // hand the same bytes to CreateBlobAsync without re-reading the
            // IBrowserFile stream (it's single-use).
            byte[] buffer;
            try
            {
                using var src = file.OpenReadStream(MaxImageBytes);
                using var ms = new MemoryStream((int)Math.Min(file.Size, MaxImageBytes));
                await src.CopyToAsync(ms, ComponentDetached);
                buffer = ms.ToArray();
            }
            catch (OperationCanceledException) { return "canceled"; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to buffer {File} for upload.", file.Name);
                return "read failed";
            }

            if (buffer.LongLength == 0) return "empty file";

            // Sniff the head: MIME first, then intrinsic dimensions.
            var head = buffer.AsSpan(0, (int)Math.Min(buffer.Length, SniffHeadLength));
            var contentType = MimeSniffer.Detect(head);
            if (contentType is null)
                return "Only PNG, JPEG, and WebP images are accepted.";

            double originalW = 0;
            double originalH = 0;
            if (ImageDimensionSniffer.TryDetect(head, contentType, out int pxW, out int pxH))
            {
                originalW = pxW / (double)cellPixels;
                originalH = pxH / (double)cellPixels;
            }

            using var content = new MemoryStream(buffer, writable: false);
            try
            {
                var result = await Library.AddImageAsync(
                    State, UserService.CurrentUser, mapId,
                    contentType, buffer.LongLength,
                    originalW, originalH,
                    content, ComponentDetached);

                if (result.TryGetSuccess(out _)) return null;
                if (result.TryGetFailure(out var err)) return err.PublicMessage;
                return "upload failed";
            }
            catch (OperationCanceledException) { return "canceled"; }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Image upload failed for {File}.", file.Name);
                return "upload failed";
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
