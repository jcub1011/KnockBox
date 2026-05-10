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

        private string PillCls(AttributePreset preset)
        {
            var current = _customMode ? AttributePreset.Custom : State.AttributeSchema.Preset;
            return current == preset ? "active" : string.Empty;
        }

        private async Task SelectPreset(AttributePreset picked)
        {
            if (UserService.CurrentUser is null) return;

            if (picked == AttributePreset.Custom)
            {
                _customMode = true;
                if (_customRows.Count == 0)
                    _customRows.Add(new CustomRowDraft());
                _customError = null;
                return;
            }

            _customMode = false;
            if (picked == State.AttributeSchema.Preset) return;
            var preset = AttributeSchema.FromPreset(picked);
            await ApplyOrPromptAsync(preset);
        }

        private async Task ApplyOrPromptAsync(AttributeSchema schema)
        {
            if (State.Phase == DndMapperPhase.Lobby)
            {
                ApplyDirect(schema);
                return;
            }

            _pendingSchema = schema;
            _cascadeOpen = true;
            await Task.CompletedTask;
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

        private async Task ConfirmCascade()
        {
            if (_pendingSchema is null) { _cascadeOpen = false; return; }
            ApplyDirect(_pendingSchema);
            _pendingSchema = null;
            _cascadeOpen = false;
            await Task.CompletedTask;
        }

        private void CancelCascade()
        {
            _pendingSchema = null;
            _cascadeOpen = false;
        }

        private void AddRow()
        {
            _customRows.Add(new CustomRowDraft());
            _customError = null;
        }

        private void RemoveRow(int idx)
        {
            if (idx >= 0 && idx < _customRows.Count) _customRows.RemoveAt(idx);
            _customError = null;
        }

        private void UpdateName(int idx, string name)
        {
            if (idx < 0 || idx >= _customRows.Count) return;
            _customRows[idx].Name = name;
        }

        private void UpdateType(int idx, string? raw)
        {
            if (idx < 0 || idx >= _customRows.Count) return;
            if (Enum.TryParse<AttributeValueType>(raw, out var type))
                _customRows[idx].Type = type;
        }

        private void UpdateDefault(int idx, string value)
        {
            if (idx < 0 || idx >= _customRows.Count) return;
            _customRows[idx].Default = value;
        }

        private async Task SaveCustom()
        {
            var rows = BuildRowsFromDrafts(out var error);
            if (rows is null)
            {
                _customError = error;
                return;
            }
            _customError = null;

            if (State.AttributeSchema.Preset == AttributePreset.Custom &&
                State.AttributeSchema.Rows.SequenceEqual(rows))
            {
                return;
            }

            var schema = new AttributeSchema(AttributePreset.Custom, rows);
            await ApplyOrPromptAsync(schema);
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
