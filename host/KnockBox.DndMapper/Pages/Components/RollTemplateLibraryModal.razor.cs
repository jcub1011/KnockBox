using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class RollTemplateLibraryModal : DisposableComponent
    {
        private static readonly int[] DieSizes = [4, 6, 8, 10, 12, 20, 100];

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public CharacterSheet? Sheet { get; set; }
        [Parameter] public User? Caller { get; set; }
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;

        private IDisposable? _stateSub;

        private string _newName = string.Empty;
        private string? _error;

        private Guid? _editingId;
        private string _editName = string.Empty;
        private List<DiceTerm> _editDice = [new DiceTerm(1, 20)];
        private int _editFlat;
        private RollMode _editMode = RollMode.Normal;
        private string? _editAttr;
        private string _editLabel = string.Empty;

        // The host always edits a per-sheet template list; players only when
        // the sheet is their own. Sheet can be null (no assigned sheet) — in
        // that case the section is read-only and the create form is hidden
        // by the razor's `Sheet is null` branch.
        private bool CanEditSheetTemplates =>
            Sheet is not null && (IsHost || Sheet.OwnerUserId == Caller?.Id);

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

        private static string SheetDisplayName(CharacterSheet sheet) =>
            string.IsNullOrWhiteSpace(sheet.CharacterName) ? "(unnamed)" : sheet.CharacterName;

        private static string Summarise(RollTemplate t)
        {
            var dice = string.Join("+", t.Dice.Select(d => $"{d.Count}d{d.Sides}"));
            var attr = string.IsNullOrEmpty(t.AttributeName) ? string.Empty : $" +{t.AttributeName}";
            var flat = t.FlatModifier == 0 ? string.Empty
                : (t.FlatModifier > 0 ? $" +{t.FlatModifier}" : $" {t.FlatModifier}");
            var mode = t.Mode switch
            {
                RollMode.Advantage => " (ADV)",
                RollMode.Disadvantage => " (DIS)",
                _ => string.Empty,
            };
            return $"{dice}{attr}{flat}{mode}";
        }

        private void BeginEdit(RollTemplate t)
        {
            _editingId = t.Id;
            _editName = t.Name;
            _editDice = [.. t.Dice];
            if (_editDice.Count == 0) _editDice.Add(new DiceTerm(1, 20));
            _editFlat = t.FlatModifier;
            _editMode = t.Mode;
            _editAttr = t.AttributeName;
            _editLabel = t.Label;
            _error = null;
        }

        private void CancelEdit()
        {
            _editingId = null;
            _error = null;
        }

        private void CommitEdit(RollTemplateScope scope)
        {
            if (_editingId is not Guid id) return;
            if (Caller is null) { _error = "No user."; return; }

            // Clamp out zero-count terms; if the user emptied the list, fall
            // back to a single d20 so the validator doesn't bounce them.
            var dice = _editDice.Where(d => d.Count > 0).ToList();
            if (dice.Count == 0) dice = [new DiceTerm(1, 20)];

            var result = scope switch
            {
                RollTemplateScope.Global =>
                    Engine.UpdateGlobalRollTemplateAsync(State, Caller, id, _editName.Trim(),
                        dice, _editFlat, _editMode, _editAttr, _editLabel),
                RollTemplateScope.Sheet when Sheet is not null =>
                    Engine.UpdateSheetRollTemplateAsync(State, Caller, Sheet.Id, id, _editName.Trim(),
                        dice, _editFlat, _editMode, _editAttr, _editLabel),
                _ => Core.Primitives.Returns.Result.FromError("Unsupported scope."),
            };
            if (result.TryGetFailure(out var err)) { _error = err.PublicMessage; return; }
            _editingId = null;
        }

        private void Delete(Guid id, RollTemplateScope scope)
        {
            if (Caller is null) return;
            var result = scope switch
            {
                RollTemplateScope.Global =>
                    Engine.DeleteGlobalRollTemplateAsync(State, Caller, id),
                RollTemplateScope.Sheet when Sheet is not null =>
                    Engine.DeleteSheetRollTemplateAsync(State, Caller, Sheet.Id, id),
                _ => Core.Primitives.Returns.Result.FromError("Unsupported scope."),
            };
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
            if (_editingId == id) _editingId = null;
        }

        private void CreateBlank(RollTemplateScope scope)
        {
            _error = null;
            if (Caller is null) { _error = "No user."; return; }
            var name = _newName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var dice = new List<DiceTerm> { new(1, 20) };
            var resultGuid = scope switch
            {
                RollTemplateScope.Global =>
                    Engine.CreateGlobalRollTemplateAsync(State, Caller, name, dice, 0, RollMode.Normal, null, name),
                RollTemplateScope.Sheet when Sheet is not null =>
                    Engine.CreateSheetRollTemplateAsync(State, Caller, Sheet.Id, name, dice, 0, RollMode.Normal, null, name),
                _ => Core.Primitives.Returns.ValueResult<Guid>.FromError("Unsupported scope."),
            };
            if (resultGuid.TryGetFailure(out var err)) { _error = err.PublicMessage; return; }
            _newName = string.Empty;
        }

        private void AddDice() => _editDice.Add(new DiceTerm(1, 20));

        private void RemoveDice(int idx)
        {
            if (idx >= 0 && idx < _editDice.Count) _editDice.RemoveAt(idx);
            if (_editDice.Count == 0) _editDice.Add(new DiceTerm(1, 20));
        }

        private void UpdateDiceCount(int idx, object? raw)
        {
            if (idx < 0 || idx >= _editDice.Count) return;
            int c = Math.Clamp(ParseInt(raw, _editDice[idx].Count), 0, 20);
            _editDice[idx] = _editDice[idx] with { Count = c };
        }

        private void UpdateDiceSides(int idx, object? raw)
        {
            if (idx < 0 || idx >= _editDice.Count) return;
            int s = ParseInt(raw, _editDice[idx].Sides);
            _editDice[idx] = _editDice[idx] with { Sides = s };
        }

        private void OnEditModeChange(string? raw) => _editMode = raw switch
        {
            "Advantage" => RollMode.Advantage,
            "Disadvantage" => RollMode.Disadvantage,
            _ => RollMode.Normal,
        };

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }
    }
}
