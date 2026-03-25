using KnockBox.Services.State.Games.DrawnToDress;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class PoolRevealPhase : ComponentBase
    {
        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;
    }
}
