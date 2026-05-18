using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class StatusEffectsPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public CharacterSheet Sheet { get; set; } = default!;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public bool CanEdit { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;

        private bool _adderOpen;
        private bool _libraryOpen;
        private string _draftName = string.Empty;
        private string _draftNotes = string.Empty;
        private int? _draftMaxHpDelta;
        private int? _draftOnApplyDelta;
        private List<AttributeDelta> _draftDeltas = new();
        private string _selectedTemplateId = string.Empty;
        private string? _adderError;

        private void OpenAdder()
        {
            _draftName = string.Empty;
            _draftNotes = string.Empty;
            _draftMaxHpDelta = null;
            _draftOnApplyDelta = null;
            _draftDeltas = new();
            _selectedTemplateId = string.Empty;
            _adderError = null;
            _adderOpen = true;
        }

        private void CloseAdder() => _adderOpen = false;

        private void OnTemplateChosen(string? value)
        {
            _selectedTemplateId = value ?? string.Empty;
            if (!Guid.TryParse(_selectedTemplateId, out var id)) return;
            var template = State.StatusEffectTemplates.FirstOrDefault(t => t.Id == id);
            if (template is null) return;
            // Clone template fields into the draft — applying decouples per §8.5.3.
            _draftName = template.Name;
            _draftNotes = template.Notes;
            _draftMaxHpDelta = template.MaxHpDelta;
            _draftOnApplyDelta = template.OnApplyHpDelta;
            _draftDeltas = [.. template.AttributeDeltas.Select(d => new AttributeDelta(d.AttributeName, d.Delta))];
            _adderError = null;
        }

        private void AddDeltaRow()
        {
            var firstAttr = State.AttributeSchema.Rows.FirstOrDefault()?.Name ?? "INT";
            _draftDeltas.Add(new AttributeDelta(firstAttr, 0));
        }

        private void RemoveDeltaRow(int idx)
        {
            if (idx >= 0 && idx < _draftDeltas.Count)
                _draftDeltas.RemoveAt(idx);
        }

        private void UpdateDeltaName(int idx, string? name)
        {
            if (idx < 0 || idx >= _draftDeltas.Count || string.IsNullOrEmpty(name)) return;
            _draftDeltas[idx] = _draftDeltas[idx] with { AttributeName = name };
        }

        private void UpdateDeltaValue(int idx, int value)
        {
            if (idx < 0 || idx >= _draftDeltas.Count) return;
            _draftDeltas[idx] = _draftDeltas[idx] with { Delta = value };
        }

        private void ApplyDraft()
        {
            _adderError = null;
            var user = UserService.CurrentUser;
            if (user is null) { _adderError = "No user."; return; }

            var result = Engine.ApplyStatusEffectAsync(
                State, user, Sheet.Id,
                _draftName.Trim(),
                _draftDeltas,
                _draftMaxHpDelta,
                _draftOnApplyDelta,
                _draftNotes);

            if (result.TryGetFailure(out var err))
            {
                _adderError = err.PublicMessage;
                return;
            }
            _adderOpen = false;
        }

        private void RemoveEffect(Guid effectId)
        {
            var user = UserService.CurrentUser;
            if (user is null) return;
            Engine.RemoveStatusEffectAsync(State, user, Sheet.Id, effectId);
        }

        private static string FormatDeltaSummary(StatusEffect effect)
        {
            var parts = new List<string>();
            foreach (var d in effect.AttributeDeltas)
            {
                var sign = d.Delta >= 0 ? "+" : "−";
                parts.Add($"{d.AttributeName} {sign}{Math.Abs(d.Delta)}");
            }
            if (effect.MaxHpDelta is int mx && mx != 0)
            {
                var sign = mx >= 0 ? "+" : "−";
                parts.Add($"MaxHP {sign}{Math.Abs(mx)}");
            }
            return parts.Count == 0 ? "no modifiers" : string.Join(" · ", parts);
        }

        private static string FormatTooltip(StatusEffect effect)
        {
            var parts = new List<string> { effect.Name };
            if (!string.IsNullOrWhiteSpace(effect.Notes)) parts.Add(effect.Notes);
            parts.Add($"Applied {effect.AppliedUtc:HH:mm}");
            return string.Join("\n", parts);
        }

        private static int ParseInt(object? value, int fallback)
        {
            if (value is null) return fallback;
            return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        }

        private static int? ParseNullableInt(object? value)
        {
            var s = value?.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        }
    }
}
