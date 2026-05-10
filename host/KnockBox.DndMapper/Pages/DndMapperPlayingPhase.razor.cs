using KnockBox.DndMapper.Pages.Components;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages
{
    public partial class DndMapperPlayingPhase : ComponentBase, IAsyncDisposable
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string RoomCode { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;

        private readonly DndMapperToastService _toasts = new();

        private Guid? _selectedImageId;
        private bool _diceOpen;
        private bool _permsOpen;
        private bool _leftCollapsed;
        private bool _rightCollapsed;

        private Map? ActiveMap =>
            State.ActiveMapId is Guid id
                ? State.Maps.FirstOrDefault(m => m.Id == id)
                : null;

        private void OnSelectedImageIdChanged(Guid? id) => _selectedImageId = id;

        private void ToggleDice() => _diceOpen = !_diceOpen;
        private void TogglePerms() => _permsOpen = !_permsOpen;
        private void ToggleLeftRail() => _leftCollapsed = !_leftCollapsed;
        private void ToggleRightRail() => _rightCollapsed = !_rightCollapsed;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
