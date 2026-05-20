using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostFogPanel : DisposableComponent
    {
        private static readonly int[] BrushSizes = { 1, 2, 3 };

        [Parameter, EditorRequired]
        public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected IFogPaintContext FogContext { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _confirmFill;
        private bool _confirmClear;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            FogContext.Changed += OnFogContextChanged;
            base.OnInitialized();
        }

        private void OnFogContextChanged() => InvokeAsync(StateHasChanged);

        private Map? ActiveMap =>
            State?.ActiveMapId is Guid id
                ? State.Maps.FirstOrDefault(m => m.Id == id)
                : null;

        private string ModeClass(FogPaintMode mode) =>
            FogContext.Mode == mode
                ? "dndm-btn dndm-btn--small dndm-btn--primary"
                : "dndm-btn dndm-btn--small";

        private string BrushClass(int size) =>
            FogContext.BrushRadius == size
                ? "dndm-btn dndm-btn--small dndm-btn--primary"
                : "dndm-btn dndm-btn--small";

        private void SetMode(FogPaintMode mode) => FogContext.Set(mode, FogContext.BrushRadius);

        private void SetBrush(int size) => FogContext.Set(FogContext.Mode, size);

        private void OnFillClicked()
        {
            if (ActiveMap is null) return;
            _confirmFill = true;
        }

        private void OnClearClicked()
        {
            if (ActiveMap is null) return;
            _confirmClear = true;
        }

        private async Task OnConfirmFill()
        {
            _confirmFill = false;
            if (UserService.CurrentUser is null || ActiveMap is null) return;
            var result = Engine.FillMapWithFogAsync(State, UserService.CurrentUser, ActiveMap.Id);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private void OnCancelFill() => _confirmFill = false;

        private async Task OnConfirmClear()
        {
            _confirmClear = false;
            if (UserService.CurrentUser is null || ActiveMap is null) return;
            var result = Engine.ClearAllFogAsync(State, UserService.CurrentUser, ActiveMap.Id);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private void OnCancelClear() => _confirmClear = false;

        public override void Dispose()
        {
            FogContext.Changed -= OnFogContextChanged;
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
