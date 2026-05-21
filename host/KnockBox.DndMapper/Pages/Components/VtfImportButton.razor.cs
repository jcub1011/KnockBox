using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Library;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class VtfImportButton : DisposableComponent
    {
        [Inject] protected DndMapperLibraryService Library { get; set; } = default!;
        [Inject] protected ILogger<VtfImportButton> Logger { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private ElementReference _fileInput;
        private bool _importing;

        private async Task OnFileSelected()
        {
            _importing = true;
            StateHasChanged();
            try
            {
                var result = await Library.ImportSlotFromInputElementAsync(_fileInput, ComponentDetached);
                if (result.IsCanceled) return;
                if (result.TryGetFailure(out var err))
                {
                    await PushToast(err.PublicMessage, DndMapperToastTone.Warning);
                    return;
                }
                await PushToast("Slot imported.", DndMapperToastTone.Success);
            }
            catch (OperationCanceledException) { /* component detached */ }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "DnD Mapper VTF import flow threw.");
                await PushToast("Import failed.", DndMapperToastTone.Danger);
            }
            finally
            {
                _importing = false;
                StateHasChanged();
            }
        }

        private Task PushToast(string message, DndMapperToastTone tone)
            => Toasts is null ? Task.CompletedTask : Toasts.Push(message, tone);
    }
}
