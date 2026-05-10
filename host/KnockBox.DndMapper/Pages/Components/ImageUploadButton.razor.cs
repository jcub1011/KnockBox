using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class ImageUploadButton : DisposableComponent
    {
        private const long MaxImageBytes = 5L * 1024 * 1024;

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<ImageUploadButton> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _uploading;

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

            var file = e.File;
            if (file is null) return;

            if (file.Size > MaxImageBytes)
            {
                await PushToast("Image exceeds 5 MB.", DndMapperToastTone.Warning);
                return;
            }

            _uploading = true;
            StateHasChanged();
            try
            {
                using var stream = file.OpenReadStream(MaxImageBytes);
                var result = await Engine.SaveImageAsync(
                    State, UserService.CurrentUser, mapId, stream, file.Size, ComponentDetached);

                if (result.TryGetSuccess(out _))
                {
                    await PushToast("Image uploaded.", DndMapperToastTone.Success);
                }
                else if (result.TryGetFailure(out var err))
                {
                    await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Image upload failed.");
                await PushToast("Upload failed.", DndMapperToastTone.Danger);
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
