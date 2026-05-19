using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class InitiativeBanner : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }

        private async Task OnRollInitiative()
        {
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.SubmitInitiativeRollAsync(State, user);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }
    }
}
