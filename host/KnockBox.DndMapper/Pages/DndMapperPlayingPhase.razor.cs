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

        private Map? ActiveMap =>
            State.ActiveMapId is Guid id
                ? State.Maps.FirstOrDefault(m => m.Id == id)
                : null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
