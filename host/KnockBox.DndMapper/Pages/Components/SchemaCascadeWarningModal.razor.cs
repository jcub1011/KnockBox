using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class SchemaCascadeWarningModal : ComponentBase
    {
        [Parameter] public bool IsOpen { get; set; }
        [Parameter] public EventCallback OnConfirm { get; set; }
        [Parameter] public EventCallback OnCancel { get; set; }
    }
}
