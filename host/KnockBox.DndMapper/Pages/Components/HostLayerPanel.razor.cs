using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostLayerPanel : DisposableComponent
    {
        [Parameter, EditorRequired]
        public DndMapperGameState State { get; set; } = default!;

        [Parameter] public string RoomCode { get; set; } = string.Empty;
        [Parameter] public Guid? SelectedImageId { get; set; }
        [Parameter] public EventCallback<Guid?> SelectedImageIdChanged { get; set; }

        private IDisposable? _stateSub;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private Map? ActiveMap
        {
            get
            {
                if (State?.ActiveMapId is null) return null;
                return State.Maps.FirstOrDefault(map => map.Id == State.ActiveMapId);
            }
        }

        private async Task SelectImage(Guid id)
        {
            await SelectedImageIdChanged.InvokeAsync(id == SelectedImageId ? null : id);
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
