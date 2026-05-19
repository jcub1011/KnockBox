using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
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
        private Guid? _expandedId;

        // Per-template draft of editable rows for user templates. Keyed by
        // template id so expanding/collapsing keeps in-progress edits.
        private readonly Dictionary<Guid, List<RowDraft>> _drafts = new();
        private readonly Dictionary<Guid, string?> _draftErrors = new();

        private sealed class RowDraft
        {
            public string Name { get; set; } = string.Empty;
            public AttributeValueType Type { get; set; } = AttributeValueType.Score;
            public string Default { get; set; } = "10";
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(OnStateChangedAsync);
            base.OnInitialized();
        }

        // Prune draft state for templates that no longer exist (Reset Session,
        // host-side deletes, library load) before re-rendering so the modal
        // doesn't hold stale entries indefinitely.
        private async ValueTask OnStateChangedAsync()
        {
            var live = State.CustomTemplates;
            if (_drafts.Count > 0)
            {
                var orphans = _drafts.Keys.Where(k => !live.ContainsKey(k)).ToList();
                foreach (var k in orphans)
                {
                    _drafts.Remove(k);
                    _draftErrors.Remove(k);
                }
            }
            if (_expandedId is Guid id && !live.ContainsKey(id)) _expandedId = null;
            await InvokeAsync(StateHasChanged);
        }

        // Content-equality match against the live AttributeSchema. A template is
        // considered "selected" if its row list (name + type + default value)
        // matches state.AttributeSchema row-for-row. Editing a selected user
        // template diverges the match, which surfaces the badge moving off
        // until the user reapplies.
        private bool IsActiveTemplate(NamedTemplate template)
        {
            var liveRows = State.AttributeSchema.Rows;
            if (liveRows.Count != template.Rows.Count) return false;
            for (int i = 0; i < liveRows.Count; i++)
            {
                var a = liveRows[i];
                var b = template.Rows[i];
                if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)) return false;
                if (a.Type != b.Type) return false;
                if (!Equals(a.Default, b.Default)) return false;
            }
            return true;
        }

        // Built-ins first (stable order by name), then user templates by name.
        private IEnumerable<KeyValuePair<Guid, NamedTemplate>> OrderedTemplates() =>
            State.CustomTemplates
                .OrderByDescending(kv => kv.Value.IsBuiltIn)
                .ThenBy(kv => kv.Value.Name, StringComparer.OrdinalIgnoreCase);

        private void ToggleExpanded(Guid id)
        {
            _expandedId = _expandedId == id ? null : id;
        }

        private List<RowDraft> GetOrInitDraft(NamedTemplate template)
        {
            if (!_drafts.TryGetValue(template.Id, out var list))
            {
                list = template.Rows.Select(ToDraft).ToList();
                _drafts[template.Id] = list;
            }
            return list;
        }

        private static RowDraft ToDraft(AttributeRow row) => new()
        {
            Name = row.Name,
            Type = row.Type,
            Default = FormatDefault(row.Default),
        };

        private static string FormatDefault(AttributeValue v) => v.Type switch
        {
            AttributeValueType.Score => v.IntValue?.ToString() ?? "10",
            AttributeValueType.Modifier => v.IntValue?.ToString() ?? "0",
            AttributeValueType.Text => v.StringValue ?? string.Empty,
            _ => string.Empty,
        };

        private void UpdateDraftName(Guid templateId, int idx, string name)
        {
            if (!_drafts.TryGetValue(templateId, out var list) || idx < 0 || idx >= list.Count) return;
            list[idx].Name = name;
            CommitDraft(templateId);
        }

        private void UpdateDraftType(Guid templateId, int idx, string? raw)
        {
            if (!_drafts.TryGetValue(templateId, out var list) || idx < 0 || idx >= list.Count) return;
            if (Enum.TryParse<AttributeValueType>(raw, out var type))
                list[idx].Type = type;
            CommitDraft(templateId);
        }

        private void UpdateDraftDefault(Guid templateId, int idx, string value)
        {
            if (!_drafts.TryGetValue(templateId, out var list) || idx < 0 || idx >= list.Count) return;
            list[idx].Default = value;
            CommitDraft(templateId);
        }

        private void AddDraftRow(Guid templateId)
        {
            if (!_drafts.TryGetValue(templateId, out var list)) return;
            list.Add(new RowDraft());
            CommitDraft(templateId);
        }

        private void RemoveDraftRow(Guid templateId, int idx)
        {
            if (!_drafts.TryGetValue(templateId, out var list) || idx < 0 || idx >= list.Count) return;
            list.RemoveAt(idx);
            CommitDraft(templateId);
        }

        // Auto-save on every committed edit (blur on text inputs fires @onchange,
        // selects fire on every change). Validates first; if invalid, the error
        // surfaces in the preview and no engine call is made.
        private void CommitDraft(Guid templateId)
        {
            if (UserService.CurrentUser is null) return;
            if (!_drafts.TryGetValue(templateId, out var list) || list.Count == 0)
            {
                _draftErrors[templateId] = "Template must have at least one row.";
                return;
            }

            var rows = BuildRowsFromDraft(list, out var error);
            if (rows is null)
            {
                _draftErrors[templateId] = error;
                return;
            }

            _draftErrors[templateId] = null;
            var result = Engine.UpdateCustomTemplateAsync(State, UserService.CurrentUser, templateId, rows);
            if (result.TryGetFailure(out var err))
            {
                _draftErrors[templateId] = err.PublicMessage;
            }
        }

        private static List<AttributeRow>? BuildRowsFromDraft(List<RowDraft> drafts, out string? error)
        {
            var rows = new List<AttributeRow>(drafts.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in drafts)
            {
                var name = d.Name?.Trim() ?? string.Empty;
                if (name.Length == 0) { error = "Row name cannot be empty."; return null; }
                if (!seen.Add(name)) { error = $"Duplicate row name '{name}'."; return null; }

                AttributeValue value;
                switch (d.Type)
                {
                    case AttributeValueType.Score:
                        if (!int.TryParse(d.Default, out var score)) { error = $"'{name}' default must be an integer."; return null; }
                        value = AttributeValue.Score(score);
                        break;
                    case AttributeValueType.Modifier:
                        if (!int.TryParse(d.Default, out var mod)) { error = $"'{name}' default must be an integer."; return null; }
                        value = AttributeValue.Modifier(mod);
                        break;
                    case AttributeValueType.Text:
                        value = AttributeValue.Text(d.Default ?? string.Empty);
                        break;
                    default:
                        error = $"Unknown attribute type for '{name}'."; return null;
                }
                rows.Add(new AttributeRow(name, d.Type, value));
            }
            error = null;
            return rows;
        }

        private NamedTemplate? OpenTemplate =>
            _expandedId is Guid id && State.CustomTemplates.TryGetValue(id, out var t) ? t : null;

        private bool CanSaveAsNew =>
            OpenTemplate is not null && !string.IsNullOrWhiteSpace(_newTemplateName);

        private string SaveAsButtonTitle => OpenTemplate is null
            ? "Open a template (click its name) to copy its rows into a new template."
            : $"Saves the rows of \"{OpenTemplate.Name}\" as a new template.";

        // Source rows for "save as new": the currently-open template. For user
        // templates this reflects any auto-saved edits already committed to
        // state. Built-ins use their seeded rows.
        private void SaveOpenAsTemplate()
        {
            if (UserService.CurrentUser is null) return;
            if (OpenTemplate is null) return;

            _templateError = null;
            var result = Engine.CreateCustomTemplateAsync(
                State, UserService.CurrentUser, _newTemplateName, OpenTemplate.Rows);
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
            // Cleanup local draft state for the deleted template.
            _drafts.Remove(id);
            _draftErrors.Remove(id);
            if (_expandedId == id) _expandedId = null;
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
