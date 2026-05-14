using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class CharacterSheetPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private Guid? _activeSheetId;

        private string _nameDraft = string.Empty;
        private string _notesDraft = string.Empty;
        private Guid? _draftFor;

        private bool _npcModalOpen;
        private string _npcNameDraft = string.Empty;

        private List<CharacterSheet> VisibleSheets =>
            [.. State.Sheets.Values
                .Where(s => SheetVisibilityHelper.CanSeeSheet(s, IsHost))
                .OrderBy(s => s.OwnerUserId == CurrentUserId ? 0 : 1)
                .ThenBy(s => s.CharacterName)];

        private CharacterSheet? ActiveSheet
        {
            get
            {
                if (_activeSheetId is Guid id && State.Sheets.TryGetValue(id, out var s)) return s;
                var own = State.Sheets.Values.FirstOrDefault(x => x.OwnerUserId == CurrentUserId);
                return own ?? VisibleSheets.FirstOrDefault();
            }
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        protected override void OnParametersSet()
        {
            EnsureDraftSynced();
            base.OnParametersSet();
        }

        private void EnsureDraftSynced()
        {
            var sheet = ActiveSheet;
            if (sheet is null)
            {
                _draftFor = null;
                _nameDraft = _notesDraft = string.Empty;
                return;
            }
            if (_draftFor != sheet.Id)
            {
                _draftFor = sheet.Id;
                _nameDraft = sheet.CharacterName;
                _notesDraft = sheet.Notes;
            }
        }

        private void SelectSheet(Guid id)
        {
            _activeSheetId = id;
            EnsureDraftSynced();
        }

        private void OnNameInput(CharacterSheet sheet, string? value)
        {
            _draftFor = sheet.Id;
            _nameDraft = value ?? string.Empty;
        }

        private void OnNotesInput(string? value)
        {
            _notesDraft = value ?? string.Empty;
        }

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }

        private static string FormatMod(int? mod) =>
            mod is null ? "—" : (mod.Value >= 0 ? $"+{mod.Value}" : mod.Value.ToString(CultureInfo.InvariantCulture));

        private async Task CommitAttribute(CharacterSheet sheet, AttributeRow row, AttributeValue value)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.UpdateSheetAttributeAsync(State, UserService.CurrentUser, sheet.Id, row.Name, value);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task CommitFreeFields(CharacterSheet sheet)
        {
            if (UserService.CurrentUser is null) return;
            var name = string.IsNullOrWhiteSpace(_nameDraft) ? sheet.CharacterName : _nameDraft.Trim();
            if (name == sheet.CharacterName && _notesDraft == sheet.Notes) return;
            var result = Engine.UpdateSheetFreeFieldsAsync(
                State, UserService.CurrentUser, sheet.Id, name, _notesDraft, sheet.Hp, sheet.MaxHp);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task InitializeHp(CharacterSheet sheet)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.UpdateSheetFreeFieldsAsync(
                State, UserService.CurrentUser, sheet.Id, sheet.CharacterName, sheet.Notes, hp: 0, maxHp: 0);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task StepHp(CharacterSheet sheet, int delta)
        {
            if (UserService.CurrentUser is null || sheet.Hp is null) return;
            var newHp = sheet.Hp.Value + delta;
            var result = Engine.UpdateSheetFreeFieldsAsync(
                State, UserService.CurrentUser, sheet.Id, sheet.CharacterName, sheet.Notes, newHp, sheet.MaxHp);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnHpDirect(CharacterSheet sheet, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            var newHp = ParseInt(raw, sheet.Hp ?? 0);
            var result = Engine.UpdateSheetFreeFieldsAsync(
                State, UserService.CurrentUser, sheet.Id, sheet.CharacterName, sheet.Notes, newHp, sheet.MaxHp);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnMaxHpDirect(CharacterSheet sheet, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            var newMax = ParseInt(raw, sheet.MaxHp ?? 0);
            var result = Engine.UpdateSheetFreeFieldsAsync(
                State, UserService.CurrentUser, sheet.Id, sheet.CharacterName, sheet.Notes, sheet.Hp, newMax);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void OpenNpcModal()
        {
            _npcNameDraft = string.Empty;
            _npcModalOpen = true;
        }

        private void CloseNpcModal() => _npcModalOpen = false;

        private async Task ConfirmCreateNpcSheet()
        {
            if (UserService.CurrentUser is null) return;
            var name = _npcNameDraft.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var result = Engine.CreateSheetAsync(State, UserService.CurrentUser, ownerUserId: null, characterName: name);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            if (result.TryGetSuccess(out var newId))
            {
                _activeSheetId = newId;
            }
            _npcModalOpen = false;
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
