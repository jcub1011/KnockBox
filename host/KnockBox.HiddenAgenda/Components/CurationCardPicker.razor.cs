using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Components
{
    public partial class CurationCardPicker : ComponentBase
    {
        [Parameter, EditorRequired] public IReadOnlyList<CurationCard> Cards { get; set; } = default!;
        [Parameter] public Action<int>? OnCardSelected { get; set; }
    }
}
