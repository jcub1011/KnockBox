using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class ImageUploadButton : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public bool Compact { get; set; }

        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<ImageUploadButton> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private ElementReference _fileInput;
        private IDisposable? _stateSub;
        private bool _uploading;

        private bool _disabled => _uploading || State.ActiveMapId is null;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private async Task OnFileSelected()
        {
            if (UserService.CurrentUser is null) return;
            if (State.ActiveMapId is not Guid mapId) return;

            // Lazy-attach so smoke-testing pages without explicit hydration
            // still work. AttachAsync is idempotent.
            var attach = await Library.AttachAsync(State, UserService.CurrentUser, ComponentDetached);
            if (attach.TryGetFailure(out var attachErr))
            {
                await PushToast(attachErr.PublicMessage, DndMapperToastTone.Warning);
                return;
            }
            if (attach.IsCanceled) return;

            _uploading = true;
            StateHasChanged();
            try
            {
                var result = await Library.UploadImagesFromInputElementAsync(
                    State, UserService.CurrentUser, mapId, _fileInput, ComponentDetached);

                if (result.IsCanceled) return;
                if (result.TryGetFailure(out var err))
                {
                    await PushToast(err.PublicMessage, DndMapperToastTone.Warning);
                    return;
                }
                if (!result.TryGetSuccess(out var outcomes) || outcomes.Count == 0)
                {
                    return;
                }

                int successes = 0;
                var failures = new List<string>(outcomes.Count);
                foreach (var outcome in outcomes)
                {
                    if (outcome.Error is null && outcome.Image is not null)
                    {
                        successes++;
                    }
                    else
                    {
                        var safeName = UploadBatchSummary.TruncateFilename(outcome.Filename);
                        var detail = $"{safeName}: {outcome.Error ?? "upload failed"}";
                        Logger.LogWarning("Image upload failed: {Detail}", detail);
                        failures.Add(detail);
                    }
                }

                var summary = UploadBatchSummary.Build(successes, failures);
                await PushToast(summary.Message, summary.Tone);
            }
            catch (OperationCanceledException) { /* component detached mid-flow */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Image upload pipeline failed.");
                await PushToast("Upload failed.", DndMapperToastTone.Warning);
            }
            finally
            {
                _uploading = false;
                StateHasChanged();
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
