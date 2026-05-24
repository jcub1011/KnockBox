using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class FloatingRollPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public DiceRollerConfig Config { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }

        // Collapsed by default — the panel lives as a floating dice-icon FAB
        // until the user opens it. State is per-circuit; no persistence.
        private bool _collapsed = true;

        private void Toggle() => _collapsed = !_collapsed;
    }
}
