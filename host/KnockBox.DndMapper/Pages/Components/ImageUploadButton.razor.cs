using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
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

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public bool Compact { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<ImageUploadButton> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _uploading;
        private int _batchDone;
        private int _batchTotal;

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
            try
            {
                using var stream = file.OpenReadStream(MaxImageBytes);
                var result = await Engine.SaveImageAsync(
                    State, UserService.CurrentUser!, mapId, stream, file.Size, ComponentDetached);

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
