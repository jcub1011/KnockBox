using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostTokenPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected Services.TokenFocusService TokenFocus { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }
        [CascadingParameter] public DndMapperViewport? Viewport { get; set; }

        private IDisposable? _stateSub;
        private Token? _pendingDelete;

        private Map? _activeMap =>
            State.ActiveMapId is Guid id ? State.Maps.FirstOrDefault(m => m.Id == id) : null;

        private List<Token> Tokens =>
            _activeMap is null
                ? []
                : [.. _activeMap.Tokens];

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        // Player tokens have OwnerUserId set at spawn. NPC-with-linked-sheet
        // tokens whose sheet is owned by a player (e.g. when a player joined,
        // got assigned an existing NPC) also count as "Player". Falls back to
        // the token's stored Type for legacy tokens with no sheet.
        private bool ResolveIsPlayer(Token t)
        {
            if (t.Type == TokenType.PlayerToken) return true;
            if (t.SheetId is Guid sid
                && State.Sheets.TryGetValue(sid, out var sheet)
                && sheet.OwnerUserId is not null)
            {
                return true;
            }
            return false;
        }

        private async Task OnToggleIcon(Token t)
        {
            if (UserService.CurrentUser is null) return;
            var next = t.IconKind == TokenIconKind.Initial ? TokenIconKind.Solid : TokenIconKind.Initial;
            var result = Engine.UpdateTokenAsync(State, UserService.CurrentUser, t.Id, t.Name, t.Color, next);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnToggleHidden(Token t)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.SetTokenHiddenAsync(State, UserService.CurrentUser, t.Id, !t.Hidden);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void OnDeleteRequest(Token t) => _pendingDelete = t;

        private void OnRowDoubleClick(Token t) => _ = TokenFocus.FocusAsync(t.Id);
        private void CancelDelete() => _pendingDelete = null;

        private async Task ConfirmDelete()
        {
            var pending = _pendingDelete;
            _pendingDelete = null;
            if (pending is null || UserService.CurrentUser is null) return;
            var result = Engine.RemoveTokenAsync(State, UserService.CurrentUser, pending.Id);
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
