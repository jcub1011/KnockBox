using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class MyTokenPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        private Token? MyToken
        {
            get
            {
                if (UserService.CurrentUser is null) return null;
                if (State.ActiveMapId is not Guid mapId) return null;
                var map = State.Maps.FirstOrDefault(m => m.Id == mapId);
                return map?.Tokens.FirstOrDefault(t =>
                    t.Type == TokenType.PlayerToken &&
                    t.OwnerUserId == UserService.CurrentUser.Id);
            }
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private async Task OnColorChanged(Token t, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            var color = raw ?? t.Color;
            if (color == t.Color) return;
            var result = Engine.UpdateTokenAsync(State, UserService.CurrentUser, t.Id, t.Name, color, t.IconKind);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
