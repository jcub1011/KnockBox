using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class SchemaPresetSelector : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _cascadeOpen;
        private AttributeSchema? _pendingSchema;
        private string? _customError;
        private bool _customMode;
        // Tracks the currently-selected preset chip in draft mode. Null until
        // the host actually clicks a preset; falls back to State.AttributeSchema.Preset
        // for the active highlight.
        private AttributePreset? _draftPreset;
        // True once the host has made any change in this session that isn't
        // yet committed; the Save Changes button enables when set.
        private bool _hasPendingChanges;

        private sealed class CustomRowDraft
        {
            public string Name { get; set; } = string.Empty;
            public AttributeValueType Type { get; set; } = AttributeValueType.Score;
            public string Default { get; set; } = "10";
        }

        private List<CustomRowDraft> _customRows = new();

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            _customMode = State.AttributeSchema.Preset == AttributePreset.Custom;
            SyncCustomRowsFromState();
            base.OnInitialized();
        }

        private bool ShowCustomEditor => _customMode || State.AttributeSchema.Preset == AttributePreset.Custom;

        // Public so the modal's Discard / Close button can ask whether a
        // confirm prompt is needed before closing.
        public bool HasPendingChanges => _hasPendingChanges;

        private void SyncCustomRowsFromState()
        {
            if (State.AttributeSchema.Preset != AttributePreset.Custom) return;
            _customRows = State.AttributeSchema.Rows
                .Select(r => new CustomRowDraft
                {
                    Name = r.Name,
                    Type = r.Type,
                    Default = FormatDefault(r.Default),
                })
                .ToList();
        }

        private static string FormatDefault(AttributeValue v) => v.Type switch
        {
            AttributeValueType.Score => v.IntValue?.ToString() ?? "10",
            AttributeValueType.Modifier => v.IntValue?.ToString() ?? "0",
            AttributeValueType.Text => v.StringValue ?? string.Empty,
            _ => string.Empty,
        };

        private AttributePreset EffectivePreset =>
            _draftPreset ?? (_customMode ? AttributePreset.Custom : State.AttributeSchema.Preset);

        private string PillCls(AttributePreset preset) =>
            EffectivePreset == preset ? "active" : string.Empty;

        private void SelectPreset(AttributePreset picked)
        {
            if (UserService.CurrentUser is null) return;

            _draftPreset = picked;
            _hasPendingChanges = true;

            if (picked == AttributePreset.Custom)
            {
                _customMode = true;
                if (_customRows.Count == 0)
                    _customRows.Add(new CustomRowDraft());
                _customError = null;
            }
            else
            {
                _customMode = false;
                _customError = null;
            }
        }

        // Resolves the user-edited draft to an AttributeSchema. Returns null if
        // validation fails (and surfaces the error via _customError).
        private AttributeSchema? BuildDraftSchema()
        {
            var preset = EffectivePreset;
            if (preset != AttributePreset.Custom)
                return AttributeSchema.FromPreset(preset);

            var rows = BuildRowsFromDrafts(out var error);
            if (rows is null)
            {
                _customError = error;
                return null;
            }
            _customError = null;
            return new AttributeSchema(AttributePreset.Custom, rows);
        }

        public async Task SaveChangesAsync()
        {
            if (UserService.CurrentUser is null) return;
            if (!_hasPendingChanges) return;

            var schema = BuildDraftSchema();
            if (schema is null) return;

            // Skip the commit if the draft matches what's already live.
            if (State.AttributeSchema.Preset == schema.Preset &&
                State.AttributeSchema.Rows.SequenceEqual(schema.Rows))
            {
                _hasPendingChanges = false;
                _draftPreset = null;
                return;
            }

            if (State.Phase == DndMapperPhase.Lobby)
            {
                ApplyDirect(schema);
                _hasPendingChanges = false;
                _draftPreset = null;
                return;
            }

            _pendingSchema = schema;
            _cascadeOpen = true;
            await Task.CompletedTask;
        }

        // Drop in-progress edits and snap the draft back to the live schema.
        public void DiscardChanges()
        {
            _draftPreset = null;
            _hasPendingChanges = false;
            _customError = null;
            _customMode = State.AttributeSchema.Preset == AttributePreset.Custom;
            SyncCustomRowsFromState();
        }

        private void ApplyDirect(AttributeSchema schema)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.ChangeSchemaAsync(State, UserService.CurrentUser, schema);
            if (result.TryGetFailure(out var err))
            {
                _ = Toasts?.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        private void ConfirmCascade()
        {
            if (_pendingSchema is null) { _cascadeOpen = false; return; }
            ApplyDirect(_pendingSchema);
            _pendingSchema = null;
            _cascadeOpen = false;
            _hasPendingChanges = false;
            _draftPreset = null;
        }

        private void CancelCascade()
        {
            _pendingSchema = null;
            _cascadeOpen = false;
        }

        private void AddRow()
        {
            _customRows.Add(new CustomRowDraft());
            MarkDirty();
        }

        private void RemoveRow(int idx)
        {
            if (idx >= 0 && idx < _customRows.Count) _customRows.RemoveAt(idx);
            MarkDirty();
        }

        private void UpdateName(int idx, string name)
        {
            if (idx < 0 || idx >= _customRows.Count) return;
            _customRows[idx].Name = name;
            MarkDirty();
        }

        private void UpdateType(int idx, string? raw)
        {
            if (idx < 0 || idx >= _customRows.Count) return;
            if (Enum.TryParse<AttributeValueType>(raw, out var type))
                _customRows[idx].Type = type;
            MarkDirty();
        }

        private void UpdateDefault(int idx, string value)
        {
            if (idx < 0 || idx >= _customRows.Count) return;
            _customRows[idx].Default = value;
            MarkDirty();
        }

        private void MarkDirty()
        {
            // Once the host starts editing rows, treat the schema as Custom in
            // the draft so a later Save commits row edits even if no preset
            // pill was clicked.
            _draftPreset = AttributePreset.Custom;
            _customMode = true;
            _hasPendingChanges = true;
            _customError = null;
        }

        private List<AttributeRow>? BuildRowsFromDrafts(out string? error)
        {
            var rows = new List<AttributeRow>(_customRows.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var draft in _customRows)
            {
                var name = draft.Name?.Trim() ?? string.Empty;
                if (name.Length == 0) { error = "Row name cannot be empty."; return null; }
                if (!seen.Add(name)) { error = $"Duplicate row name '{name}'."; return null; }

                AttributeValue value;
                switch (draft.Type)
                {
                    case AttributeValueType.Score:
                        if (!int.TryParse(draft.Default, out var score)) { error = $"'{name}' default must be an integer."; return null; }
                        value = AttributeValue.Score(score);
                        break;
                    case AttributeValueType.Modifier:
                        if (!int.TryParse(draft.Default, out var mod)) { error = $"'{name}' default must be an integer."; return null; }
                        value = AttributeValue.Modifier(mod);
                        break;
                    case AttributeValueType.Text:
                        value = AttributeValue.Text(draft.Default ?? string.Empty);
                        break;
                    default:
                        error = $"Unknown attribute type for '{name}'."; return null;
                }
                rows.Add(new AttributeRow(name, draft.Type, value));
            }
            error = null;
            return rows;
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
