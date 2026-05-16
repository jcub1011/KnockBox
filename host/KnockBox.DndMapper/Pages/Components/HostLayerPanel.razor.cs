using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
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

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

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

        // Hidden layers must not appear in the host's selection cycle — clicking
        // a hidden row is a no-op (the eye button is still active, so the host
        // can still un-hide it).
        private async Task OnRowClick(MapImage img)
        {
            if (img.Hidden) return;
            await SelectImage(img.Id);
        }

        private async Task ToggleLocked(MapImage img)
        {
            if (UserService.CurrentUser is null || State.ActiveMapId is not Guid mapId) return;
            var result = Engine.SetImageLockedAsync(State, UserService.CurrentUser, mapId, img.Id, !img.Locked);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private async Task ToggleHidden(MapImage img)
        {
            if (UserService.CurrentUser is null || State.ActiveMapId is not Guid mapId) return;
            var nextHidden = !img.Hidden;
            var result = Engine.SetImageHiddenAsync(State, UserService.CurrentUser, mapId, img.Id, nextHidden);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            // If we just hid the selected image, clear the selection so the
            // inspector dismisses.
            if (nextHidden && SelectedImageId == img.Id)
            {
                await SelectedImageIdChanged.InvokeAsync(null);
            }
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
