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

        private (double? X, double? Y) SpawnAnchor() =>
            _activeMap is not null && Viewport?.GetCenterFor(_activeMap.Id) is { } center
                ? (center.X, center.Y)
                : (null, null);

        private async Task OnAddNpc()
        {
            if (UserService.CurrentUser is null || _activeMap is null) return;
            var name = $"NPC {_activeMap.Tokens.Count(t => t.Type == TokenType.NPCToken) + 1}";
            var (ax, ay) = SpawnAnchor();
            var result = Engine.SpawnNpcTokenAsync(State, UserService.CurrentUser, _activeMap.Id, name, representsUserId: null, ax, ay);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnNameChanged(Token t, string? raw)
        {
            var newName = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(newName) || newName == t.Name) return;
            await CommitTokenUpdate(t, newName, t.Color, t.IconKind);
        }

        private async Task OnColorChanged(Token t, string? raw)
        {
            var color = raw ?? t.Color;
            if (color == t.Color) return;
            await CommitTokenUpdate(t, t.Name, color, t.IconKind);
        }

        private async Task OnToggleIcon(Token t)
        {
            var next = t.IconKind == TokenIconKind.Initial ? TokenIconKind.Solid : TokenIconKind.Initial;
            await CommitTokenUpdate(t, t.Name, t.Color, next);
        }

        private async Task CommitTokenUpdate(Token t, string name, string color, TokenIconKind iconKind)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.UpdateTokenAsync(State, UserService.CurrentUser, t.Id, name, color, iconKind);
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

        private async Task OnSheetChanged(Token t, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            Guid? newSheetId = Guid.TryParse(raw, out var id) ? id : null;
            if (newSheetId == t.SheetId) return;
            var result = Engine.SetTokenSheetAsync(State, UserService.CurrentUser, t.Id, newSheetId);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnRepresentsChanged(Token t, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            string? newRepresents = string.IsNullOrEmpty(raw) ? null : raw;
            if (newRepresents == t.RepresentsUserId) return;
            var result = Engine.SetTokenRepresentsAsync(State, UserService.CurrentUser, t.Id, newRepresents);
            if (result.TryGetFailure(out var err))
            {
                await PushToast(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        // Assign an NPC token (and its attached sheet, if any) to a
        // registered player in one atomic operation — the symmetric counterpart
        // of the auto-orphan that runs when a player leaves mid-session.
        private async Task OnAssignToPlayer(Token t, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            if (string.IsNullOrEmpty(raw)) return;
            var result = Engine.AssignCharacterToPlayerAsync(State, UserService.CurrentUser, t.Id, raw);
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
