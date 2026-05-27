using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

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

        // Per-field local draft state. Edits update these synchronously so the
        // input feels responsive; a debounced timer commits to the engine after
        // ~300ms of idle (or immediately on blur). This pattern eliminates the
        // notification cascade on every keystroke / value change.
        private readonly Dictionary<string, string> _attrDrafts = new(StringComparer.Ordinal);
        private string _notesDraft = string.Empty;
        private string _hpDraft = string.Empty;
        private string _maxHpDraft = string.Empty;
        private string _acDraft = string.Empty;
        private Guid? _draftFor;

        // Debounce machinery. Any edit schedules a single "commit all dirty
        // drafts" pass after DebounceMs of idle. Multiple field edits inside
        // the same debounce window all land on the engine together, so
        // editing notes-then-HP doesn't drop the notes edit (which a single-
        // slot last-commit-wins design would do). _dirty flips on edit and
        // clears on commit; flushing (blur, sheet switch, dispose) runs the
        // pass immediately.
        private const int DebounceMs = 300;
        private System.Threading.Timer? _commitTimer;
        private bool _dirty;

        // Click-to-edit toggle for the markdown Notes field. Default is the
        // rendered Markdown; clicking the rendered area flips to the textarea,
        // blur flips back (after committing the draft).
        private bool _notesEditing;
        private ElementReference _notesTextareaRef;
        private bool _notesFocusPending;

        private bool _npcModalOpen;
        private string _npcNameDraft = string.Empty;
        private bool _sheetSettingsOpen;

        private Guid? _pendingDeleteSheet;
        private string _pendingDeleteSheetName = string.Empty;

        // Inline-rename state: dbl-click a tab (or click the rename icon in the
        // dropdown variant) sets _renamingSheetId; the matching tab renders an
        // input bound to _renameSheetDraft. Enter / blur commits; Escape cancels.
        private Guid? _renamingSheetId;
        private string _renameSheetDraft = string.Empty;
        private ElementReference _renameInputRef;
        private bool _renameFocusPending;

        private bool HasOwnSheet =>
            State.Sheets.Values.Any(s => s.OwnerUserId == CurrentUserId);

        // Sheets the current viewer is allowed to see, filtered by map scope.
        // A sheet with ScopedMapId == null is global (visible on every map);
        // a sheet with ScopedMapId set only shows when that map is active. This
        // is host policy as well as player policy — a host editing on a
        // different map shouldn't see a sheet scoped to map X.
        private List<CharacterSheet> VisibleSheets =>
            [.. State.Sheets.Values
                .Where(s => SheetVisibilityHelper.CanSeeSheet(s, CurrentUserId, IsHost, State.Settings.PlayersCanSeeOtherSheets))
                .Where(s => s.ScopedMapId is null || s.ScopedMapId == State.ActiveMapId)
                .OrderBy(s => s.OwnerUserId == CurrentUserId ? 0 : 1)
                .ThenBy(s => s.CharacterName)];

        private CharacterSheet? ActiveSheet
        {
            get
            {
                if (_activeSheetId is Guid id
                    && State.Sheets.TryGetValue(id, out var s)
                    && (s.ScopedMapId is null || s.ScopedMapId == State.ActiveMapId))
                {
                    return s;
                }
                var own = State.Sheets.Values
                    .Where(x => x.ScopedMapId is null || x.ScopedMapId == State.ActiveMapId)
                    .FirstOrDefault(x => x.OwnerUserId == CurrentUserId);
                return own ?? VisibleSheets.FirstOrDefault();
            }
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_renameFocusPending && _renamingSheetId is not null)
            {
                _renameFocusPending = false;
                try { await _renameInputRef.FocusAsync(preventScroll: true); }
                catch { /* element not yet attached / circuit teardown */ }
            }
            if (_notesFocusPending && _notesEditing)
            {
                _notesFocusPending = false;
                try { await _notesTextareaRef.FocusAsync(preventScroll: true); }
                catch { /* element not yet attached / circuit teardown */ }
            }
            await base.OnAfterRenderAsync(firstRender);
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
                _notesDraft = string.Empty;
                _hpDraft = string.Empty;
                _maxHpDraft = string.Empty;
                _acDraft = string.Empty;
                _attrDrafts.Clear();
                return;
            }
            if (_draftFor != sheet.Id)
            {
                // Sheet switched — flush any pending commit for the old sheet
                // before we replace the drafts. Otherwise a debounced notes
                // change could land on the wrong sheet.
                _ = FlushPendingCommitAsync();
                _draftFor = sheet.Id;
                _notesDraft = sheet.Notes;
                _hpDraft = sheet.Hp?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                _maxHpDraft = sheet.MaxHp?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                _acDraft = sheet.ArmorClass?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                _attrDrafts.Clear();
            }
        }

        // Mark drafts dirty and (re)arm the debounce. Multiple field edits in
        // the same window all land in the same commit pass.
        private void ScheduleDebouncedCommit()
        {
            _dirty = true;
            if (_commitTimer is null)
            {
                _commitTimer = new System.Threading.Timer(async _ => await OnTimerFireAsync(),
                    null, Timeout.Infinite, Timeout.Infinite);
            }
            _commitTimer.Change(DebounceMs, Timeout.Infinite);
        }

        private async Task OnTimerFireAsync()
        {
            if (!_dirty) return;
            try { await InvokeAsync(CommitAllDirtyDraftsAsync); }
            catch (Exception) { /* commit errors surface via Toasts inside the commit itself */ }
        }

        // Run any pending debounced commit now (e.g. on @onblur). Safe to call
        // repeatedly — the second call is a no-op when nothing's dirty.
        private async Task FlushPendingCommitAsync()
        {
            _commitTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            if (!_dirty) return;
            try { await CommitAllDirtyDraftsAsync(); }
            catch (Exception) { /* commit errors surface via Toasts inside the commit itself */ }
        }

        // Walk every draft against the live sheet and emit only the engine
        // calls whose values actually changed. Each call is independent so a
        // failure on one (e.g. validation) doesn't roll back the others.
        private async Task CommitAllDirtyDraftsAsync()
        {
            _dirty = false;
            var sheet = ActiveSheet;
            if (sheet is null || _draftFor != sheet.Id || UserService.CurrentUser is null) return;
            var caller = UserService.CurrentUser;

            // Attributes (per-row).
            if (_attrDrafts.Count > 0)
            {
                foreach (var kv in _attrDrafts.ToList())
                {
                    var row = State.AttributeSchema.Rows.FirstOrDefault(r => string.Equals(r.Name, kv.Key, StringComparison.Ordinal));
                    if (row is null) continue;
                    var current = sheet.Values.TryGetValue(row.Name, out var cur) ? cur : row.Default;
                    AttributeValue next = row.Type switch
                    {
                        AttributeValueType.Score => AttributeValue.Score(ParseInt(kv.Value, current.IntValue ?? 10)),
                        AttributeValueType.Modifier => AttributeValue.Modifier(ParseInt(kv.Value, current.IntValue ?? 0)),
                        AttributeValueType.Text => AttributeValue.Text(kv.Value ?? string.Empty),
                        _ => current,
                    };
                    if (AttributeValueEqual(next, current)) continue;
                    var r = Engine.UpdateSheetAttributeAsync(State, caller, sheet.Id, row.Name, next);
                    if (r.TryGetFailure(out var aerr) && Toasts is not null)
                        await Toasts.Push(aerr.PublicMessage, DndMapperToastTone.Danger);
                }
            }

            // Free fields (notes + HP + MaxHp) — committed together since the
            // engine signature is one call. CharacterName isn't part of the
            // sheet panel inputs; rename has its own immediate-commit path.
            var newNotes = _notesDraft;
            int? newHp = sheet.Hp;
            int? newMax = sheet.MaxHp;
            if (sheet.Hp is not null) newHp = ParseInt(_hpDraft, sheet.Hp ?? 0);
            if (sheet.MaxHp is not null) newMax = ParseInt(_maxHpDraft, sheet.MaxHp ?? 0);
            if (newNotes != sheet.Notes || newHp != sheet.Hp || newMax != sheet.MaxHp)
            {
                var r = Engine.UpdateSheetFreeFieldsAsync(
                    State, caller, sheet.Id, sheet.CharacterName, newNotes, newHp, newMax);
                if (r.TryGetFailure(out var ferr) && Toasts is not null)
                    await Toasts.Push(ferr.PublicMessage, DndMapperToastTone.Danger);
            }

            // AC has its own engine method (no piggyback on free-fields).
            var newAc = ParseNullableInt(_acDraft);
            if (newAc != sheet.ArmorClass)
            {
                var r = Engine.UpdateSheetArmorClassAsync(State, caller, sheet.Id, newAc);
                if (r.TryGetFailure(out var acerr) && Toasts is not null)
                    await Toasts.Push(acerr.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private static bool AttributeValueEqual(AttributeValue a, AttributeValue b)
        {
            if (a.Type != b.Type) return false;
            return a.Type switch
            {
                AttributeValueType.Text => string.Equals(a.StringValue ?? string.Empty, b.StringValue ?? string.Empty, StringComparison.Ordinal),
                _ => a.IntValue == b.IntValue,
            };
        }

        // Public so sibling components (the map canvas via the playing-phase
        // page) can switch which sheet this panel is showing — e.g. when the
        // user double-clicks a token on the map.
        public void SelectSheet(Guid id)
        {
            _activeSheetId = id;
            EnsureDraftSynced();
            // Drop edit mode if we're switching sheets — otherwise the textarea
            // would briefly show the new sheet's notes in edit state.
            _notesEditing = false;
            StateHasChanged();
        }

        private void EnterNotesEdit()
        {
            _notesEditing = true;
            _notesFocusPending = true;
        }

        private async Task OnNotesBlur()
        {
            _notesEditing = false;
            await FlushPendingCommitAsync();
        }

        private void OnSheetDropdownChanged(string? raw)
        {
            if (Guid.TryParse(raw, out var id)) SelectSheet(id);
        }

        private void OnNotesInput(string? value)
        {
            _notesDraft = value ?? string.Empty;
            ScheduleDebouncedCommit();
        }

        private void OnHpInput(string? value)
        {
            _hpDraft = value ?? string.Empty;
            ScheduleDebouncedCommit();
        }

        private void OnMaxHpInput(string? value)
        {
            _maxHpDraft = value ?? string.Empty;
            ScheduleDebouncedCommit();
        }

        private void OnAcInput(string? value)
        {
            _acDraft = value ?? string.Empty;
            ScheduleDebouncedCommit();
        }

        private void OnAttributeInput(CharacterSheet sheet, AttributeRow row, string? value)
        {
            _attrDrafts[row.Name] = value ?? string.Empty;
            ScheduleDebouncedCommit();
        }

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }

        private static int? ParseNullableInt(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        }

        private static string FormatMod(int? mod) =>
            mod is null ? "—" : (mod.Value >= 0 ? $"+{mod.Value}" : mod.Value.ToString(CultureInfo.InvariantCulture));

        private static string FormatMod(int mod) =>
            mod >= 0 ? $"+{mod}" : mod.ToString(CultureInfo.InvariantCulture);

        // Tooltip for the Max HP effective indicator — lists every StatusEffect
        // whose MaxHpDelta is non-zero with name + signed delta.
        private static string FormatMaxHpBreakdown(CharacterSheet sheet)
        {
            var lines = new List<string>();
            foreach (var effect in sheet.StatusEffects)
            {
                if (effect.MaxHpDelta is not int d || d == 0) continue;
                var sign = d >= 0 ? "+" : "−";
                lines.Add($"{effect.Name}: {sign}{Math.Abs(d)}");
            }
            return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
        }

        // Tooltip for the Status Effects column — first entry is the base
        // value, the rest are per-effect deltas in encounter order. Drop the
        // base line when there are no deltas (the cell shows "—" already).
        private static string FormatBreakdown(IReadOnlyList<ContributionEntry> entries)
        {
            if (entries is null || entries.Count <= 1) return string.Empty;
            var lines = new List<string>(entries.Count);
            // Index 0 is the base contributor: "ATTR: 14".
            lines.Add($"Base: {entries[0].Delta}");
            for (int i = 1; i < entries.Count; i++)
            {
                var e = entries[i];
                var sign = e.Delta >= 0 ? "+" : "−";
                lines.Add($"{e.Source}: {sign}{Math.Abs(e.Delta)}");
            }
            return string.Join("\n", lines);
        }

        private void BeginSheetRename(CharacterSheet sheet)
        {
            _renamingSheetId = sheet.Id;
            _renameSheetDraft = sheet.CharacterName ?? string.Empty;
            _renameFocusPending = true;
        }

        private void CancelSheetRename()
        {
            _renamingSheetId = null;
            _renameSheetDraft = string.Empty;
        }

        private void OnRenameInput(ChangeEventArgs e)
        {
            _renameSheetDraft = e.Value as string ?? string.Empty;
        }

        private async Task OnRenameKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") await CommitSheetRename();
            else if (e.Key == "Escape") CancelSheetRename();
        }

        private async Task CommitSheetRename()
        {
            if (_renamingSheetId is not Guid id)
            {
                CancelSheetRename();
                return;
            }
            if (UserService.CurrentUser is null
                || !State.Sheets.TryGetValue(id, out var sheet))
            {
                CancelSheetRename();
                return;
            }
            var trimmed = (_renameSheetDraft ?? string.Empty).Trim();
            var newName = string.IsNullOrEmpty(trimmed) ? sheet.CharacterName : trimmed;
            if (newName != sheet.CharacterName)
            {
                var result = Engine.UpdateSheetFreeFieldsAsync(
                    State, UserService.CurrentUser, sheet.Id, newName, sheet.Notes, sheet.Hp, sheet.MaxHp);
                if (result.TryGetFailure(out var err) && Toasts is not null)
                {
                    await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                }
            }
            CancelSheetRename();
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

        private async Task OnColorChange(CharacterSheet sheet, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            var color = (raw ?? string.Empty).Trim();
            if (color == sheet.Color) return;
            var result = Engine.UpdateSheetColorAsync(State, UserService.CurrentUser, sheet.Id, color);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnScopeChange(CharacterSheet sheet, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            Guid? scopedTo = null;
            if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var mid))
                scopedTo = mid;
            if (scopedTo == sheet.ScopedMapId) return;
            var result = Engine.UpdateSheetScopeAsync(State, UserService.CurrentUser, sheet.Id, scopedTo);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private async Task OnDuplicateSheet(CharacterSheet sheet)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.DuplicateSheetAsync(State, UserService.CurrentUser, sheet.Id);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            if (result.TryGetSuccess(out var newId))
            {
                _activeSheetId = newId;
            }
        }

        private async Task OnCreateTokenForSheet(CharacterSheet sheet)
        {
            if (UserService.CurrentUser is null) return;
            if (State.ActiveMapId is not Guid mapId) return;
            var result = Engine.CreateTokenForSheetOnMapAsync(State, UserService.CurrentUser, sheet.Id, mapId);
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

        private void OpenSheetSettings() => _sheetSettingsOpen = true;

        private void CloseNpcModal() => _npcModalOpen = false;

        private async Task CreateMySheet()
        {
            if (UserService.CurrentUser is null) return;
            var name = string.IsNullOrWhiteSpace(UserService.CurrentUser.Name)
                ? "My character"
                : UserService.CurrentUser.Name;

            var result = Engine.CreateSheetAsync(
                State,
                UserService.CurrentUser,
                ownerUserId: UserService.CurrentUser.Id,
                characterName: name);
            if (result.TryGetFailure(out var err))
            {
                if (Toasts is not null) await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
                return;
            }
            if (result.TryGetSuccess(out var newId))
            {
                _activeSheetId = newId;
                return;
            }
            if (Toasts is not null) await Toasts.Push("Sheet creation was canceled.", DndMapperToastTone.Warning);
        }

        private async Task OnAssignSheetToPlayer(CharacterSheet sheet, string? raw)
        {
            if (UserService.CurrentUser is null) return;
            if (string.IsNullOrEmpty(raw)) return;
            var result = Engine.AssignSheetToPlayerAsync(State, UserService.CurrentUser, sheet.Id, raw);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void RequestDeleteSheet(CharacterSheet sheet)
        {
            _pendingDeleteSheet = sheet.Id;
            _pendingDeleteSheetName = string.IsNullOrWhiteSpace(sheet.CharacterName)
                ? "(unnamed)" : sheet.CharacterName;
        }

        private void CancelDeleteSheet()
        {
            _pendingDeleteSheet = null;
            _pendingDeleteSheetName = string.Empty;
        }

        private async Task ConfirmDeleteSheet()
        {
            if (UserService.CurrentUser is null || _pendingDeleteSheet is not Guid id)
            {
                CancelDeleteSheet();
                return;
            }
            var result = Engine.DeleteSheetAsync(State, UserService.CurrentUser, id);
            if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
            else if (_activeSheetId == id)
            {
                _activeSheetId = null;
            }
            CancelDeleteSheet();
        }

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

        // List of (token, map) pairs across all maps that reference the given sheet.
        // Host-only feature; ordering is by map ListOrder then token name so the
        // assigned-tokens table is stable across re-renders.
        private List<(Token Token, Map Map)> TokensForSheet(Guid sheetId)
        {
            var result = new List<(Token, Map)>();
            foreach (var map in State.Maps.OrderBy(m => m.ListOrder))
            {
                foreach (var token in map.Tokens)
                {
                    if (token.SheetId == sheetId)
                        result.Add((token, map));
                }
            }
            return result;
        }

        public override void Dispose()
        {
            // Best-effort flush before teardown: a final blur right before the
            // user closes the panel shouldn't lose their last edit.
            _ = FlushPendingCommitAsync();
            _commitTimer?.Dispose();
            _commitTimer = null;
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
