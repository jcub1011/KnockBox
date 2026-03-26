using KnockBox.Services.State.Games.DrawnToDress;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class PoolRevealPhase : ComponentBase
    {
        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        /// <summary>Heading shown in the countdown card. Defaults to "Get Ready!".</summary>
        [Parameter] public string HeadingText { get; set; } = "Get Ready!";

        /// <summary>Sub-text shown below the heading. Defaults to the Outfit 1 message.</summary>
        [Parameter] public string SubText { get; set; } = "Outfit building starts in\u2026";
    }
}
