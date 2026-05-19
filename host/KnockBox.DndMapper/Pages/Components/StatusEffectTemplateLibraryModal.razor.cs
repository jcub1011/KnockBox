using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class StatusEffectTemplateLibraryModal : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;

        private IDisposable? _stateSub;

        private string _newName = string.Empty;
        private string? _error;

        private Guid? _editingId;
        private string _editNameDraft = string.Empty;
        private string _editNotes = string.Empty;
        private int? _editMaxHp;
        private int? _editOnApply;
        private List<AttributeDelta> _editDeltas = new();

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }

        private void CreateBlankTemplate()
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) { _error = "No user."; return; }

            var result = Engine.CreateStatusEffectTemplateAsync(
                State, user, _newName.Trim(),
                [], null, null, string.Empty);
            if (result.TryGetFailure(out var err)) { _error = err.PublicMessage; return; }
            _newName = string.Empty;
        }

        private void BeginTemplateEdit(StatusEffectTemplate t)
        {
            _editingId = t.Id;
            _editNameDraft = t.Name;
            _editNotes = t.Notes;
            _editMaxHp = t.MaxHpDelta;
            _editOnApply = t.OnApplyHpDelta;
            _editDeltas = [.. t.AttributeDeltas.Select(d => new AttributeDelta(d.AttributeName, d.Delta))];
            _error = null;
        }

        private void CancelTemplateEdit()
        {
            _editingId = null;
            _error = null;
        }

        private void CommitTemplateEdit()
        {
            if (_editingId is not Guid id) return;
            var user = UserService.CurrentUser;
            if (user is null) return;

            var result = Engine.UpdateStatusEffectTemplateAsync(
                State, user, id,
                _editNameDraft.Trim(),
                _editDeltas,
                _editMaxHp,
                _editOnApply,
                _editNotes);
            if (result.TryGetFailure(out var err)) { _error = err.PublicMessage; return; }
            _editingId = null;
        }

        private void DeleteTemplate(Guid id)
        {
            var user = UserService.CurrentUser;
            if (user is null) return;
            Engine.DeleteStatusEffectTemplateAsync(State, user, id);
            if (_editingId == id) _editingId = null;
        }

        private void AddEditDelta()
        {
            var firstAttr = State.AttributeSchema.Rows.FirstOrDefault()?.Name ?? "INT";
            _editDeltas.Add(new AttributeDelta(firstAttr, 0));
        }

        private void RemoveEditDelta(int idx)
        {
            if (idx >= 0 && idx < _editDeltas.Count) _editDeltas.RemoveAt(idx);
        }

        private void UpdateEditDeltaName(int idx, string? name)
        {
            if (idx < 0 || idx >= _editDeltas.Count || string.IsNullOrEmpty(name)) return;
            _editDeltas[idx] = _editDeltas[idx] with { AttributeName = name };
        }

        private void UpdateEditDeltaValue(int idx, object? raw)
        {
            if (idx < 0 || idx >= _editDeltas.Count) return;
            var n = int.TryParse(raw?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            _editDeltas[idx] = _editDeltas[idx] with { Delta = n };
        }

        private static string SummariseTemplate(StatusEffectTemplate t)
        {
            var parts = new List<string>();
            foreach (var d in t.AttributeDeltas)
            {
                var sign = d.Delta >= 0 ? "+" : "−";
                parts.Add($"{d.AttributeName}{sign}{Math.Abs(d.Delta)}");
            }
            if (t.MaxHpDelta is int mx && mx != 0)
            {
                var sign = mx >= 0 ? "+" : "−";
                parts.Add($"MaxHP{sign}{Math.Abs(mx)}");
            }
            if (t.OnApplyHpDelta is int oh && oh != 0)
            {
                var sign = oh >= 0 ? "+" : "−";
                parts.Add($"HP{sign}{Math.Abs(oh)} once");
            }
            return parts.Count == 0 ? "(empty)" : string.Join(" · ", parts);
        }

        private static int? ParseNullableInt(object? value)
        {
            var s = value?.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        }
    }
}
