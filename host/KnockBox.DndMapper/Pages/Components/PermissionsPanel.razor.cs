using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class PermissionsPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public bool Embedded { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private static string PillCls(bool active) => active ? "active" : string.Empty;

        private async Task SetTokenMovement(TokenMovementPolicy v)
        {
            var next = State.Settings.Clone();
            next.TokenMovement = v;
            await Apply(next);
        }

        private async Task SetSheetEdit(SheetEditPolicy v)
        {
            var next = State.Settings.Clone();
            next.SheetEditByOthers = v;
            await Apply(next);
        }

        private async Task SetRollsVisible(bool v)
        {
            var next = State.Settings.Clone();
            next.RollsVisibleToPlayers = v;
            await Apply(next);
        }

        private async Task SetPlayersCanCreateNpcs(bool v)
        {
            var next = State.Settings.Clone();
            next.PlayersCanCreateNPCs = v;
            await Apply(next);
        }

        private async Task Apply(Services.State.Games.Data.DndMapperSettings next)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.UpdateSettingsAsync(State, UserService.CurrentUser, next);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private Task PushToast(string message, DndMapperToastTone tone)
            => Toasts is null ? Task.CompletedTask : Toasts.Push(message, tone);

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
