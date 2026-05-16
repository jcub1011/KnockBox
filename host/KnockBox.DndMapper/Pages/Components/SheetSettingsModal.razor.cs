using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class SheetSettingsModal : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private string _newTemplateName = string.Empty;
        private string? _templateError;
        private SchemaPresetSelector? _schemaSelector;

        private async Task SaveSchemaChanges()
        {
            if (_schemaSelector is null) return;
            await _schemaSelector.SaveChangesAsync();
        }

        private async Task DiscardAndClose()
        {
            _schemaSelector?.DiscardChanges();
            await OnClose.InvokeAsync();
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private async Task SaveCurrentAsTemplate()
        {
            if (UserService.CurrentUser is null) return;
            _templateError = null;
            var result = Engine.SaveCustomTemplateAsync(State, UserService.CurrentUser, _newTemplateName);
            if (result.TryGetFailure(out var err))
            {
                _templateError = err.PublicMessage;
                return;
            }
            _newTemplateName = string.Empty;
        }

        private async Task ApplyTemplate(Guid id)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.ApplyCustomTemplateAsync(State, UserService.CurrentUser, id);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private async Task DeleteTemplate(Guid id)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.DeleteCustomTemplateAsync(State, UserService.CurrentUser, id);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        private async Task SetHpTracking(bool enabled)
        {
            if (UserService.CurrentUser is null) return;
            if (State.Settings.HpTrackingEnabled == enabled) return;
            var next = State.Settings.Clone();
            next.HpTrackingEnabled = enabled;
            var result = Engine.UpdateSettingsAsync(State, UserService.CurrentUser, next);
            if (result.TryGetFailure(out var err) && Toasts is not null)
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
