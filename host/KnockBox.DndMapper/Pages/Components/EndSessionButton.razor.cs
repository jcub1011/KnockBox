using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class EndSessionButton : ComponentBase
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private bool _open;

        private bool IsHost() => UserService.CurrentUser is not null
            && UserService.CurrentUser.Id == State.Host.Id;

        private async Task DoEnd()
        {
            if (UserService.CurrentUser is null) return;

            var result = Engine.EndSessionAsync(State, UserService.CurrentUser);
            _open = false;
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null)
                    await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }
    }
}
