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
    public partial class DiceRollerModal : DisposableComponent
    {
        private static readonly int[] DieSizes = [4, 6, 8, 10, 12, 20, 100];

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        private List<DiceTerm> _terms = [new DiceTerm(1, 20)];
        private CharacterSheet? _pickerSheet;
        private List<CharacterSheet> _pickableSheets = [];
        private string? _attributeName;
        private int _flatMod;
        private RollMode _mode = RollMode.Normal;
        private string _label = string.Empty;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            ResolvePickableSheets();
            base.OnInitialized();
        }

        protected override void OnParametersSet()
        {
            ResolvePickableSheets();
            base.OnParametersSet();
        }

        private void ResolvePickableSheets()
        {
            _pickableSheets = IsHost
                ? [.. State.Sheets.Values.OrderBy(s => s.CharacterName)]
                : [.. State.Sheets.Values.Where(s => s.OwnerUserId == CurrentUserId).OrderBy(s => s.CharacterName)];

            _pickerSheet ??= _pickableSheets.FirstOrDefault(s => s.OwnerUserId == CurrentUserId)
                          ?? _pickableSheets.FirstOrDefault();
        }

        private int TotalDiceCount => _terms.Sum(t => Math.Max(0, t.Count));
        private bool CanAdvDis => _terms.Count == 1 && _terms[0].Count == 1 && _terms[0].Sides == 20;
        private bool CanRollInitiative => _pickerSheet is not null
            && State.AttributeSchema.Rows.Any(r => r.Name.Equals("DEX", StringComparison.OrdinalIgnoreCase));

        private bool CanSubmit => TotalDiceCount > 0 && TotalDiceCount <= 20;

        private static string PillCls(bool active) => active ? "active" : string.Empty;

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }

        private void AddTerm() => _terms.Add(new DiceTerm(1, 6));

        private void RemoveTerm(int idx)
        {
            if (idx >= 0 && idx < _terms.Count) _terms.RemoveAt(idx);
            if (_terms.Count == 0) _terms.Add(new DiceTerm(1, 20));
            if (!CanAdvDis) _mode = RollMode.Normal;
        }

        private void UpdateTermCount(int idx, int count)
        {
            count = Math.Clamp(count, 0, 20);
            _terms[idx] = _terms[idx] with { Count = count };
            if (!CanAdvDis) _mode = RollMode.Normal;
        }

        private void UpdateTermSides(int idx, int sides)
        {
            _terms[idx] = _terms[idx] with { Sides = sides };
            if (!CanAdvDis) _mode = RollMode.Normal;
        }

        private void OnAttributeChange(string? raw)
        {
            _attributeName = string.IsNullOrEmpty(raw) ? null : raw;
        }

        private void OnSheetChange(string? raw)
        {
            if (Guid.TryParse(raw, out var id))
            {
                _pickerSheet = _pickableSheets.FirstOrDefault(s => s.Id == id) ?? _pickerSheet;
            }
        }

        private async Task QuickRoll(int count, int sides, string label)
        {
            _terms = [new DiceTerm(count, sides)];
            _attributeName = null;
            _flatMod = 0;
            _mode = RollMode.Normal;
            _label = label;
            await Submit();
        }

        private async Task RollInitiative()
        {
            _terms = [new DiceTerm(1, 20)];
            _attributeName = "DEX";
            _flatMod = 0;
            _mode = RollMode.Normal;
            _label = "Initiative";
            await Submit();
        }

        private Task OnSubmit() => Submit();

        private async Task Submit()
        {
            if (UserService.CurrentUser is null) return;
            if (!CanSubmit)
            {
                if (Toasts is not null) await Toasts.Push("Total dice must be 1–20.", DndMapperToastTone.Warning);
                return;
            }

            AttributeRef? attrRef = null;
            if (_pickerSheet is not null && !string.IsNullOrEmpty(_attributeName))
            {
                attrRef = new AttributeRef(_pickerSheet.Id, _attributeName!);
            }

            var request = new RollRequest(
                Dice: [.. _terms.Where(t => t.Count > 0)],
                AttributeRef: attrRef,
                FlatModifier: _flatMod,
                Mode: _mode,
                Label: string.IsNullOrWhiteSpace(_label) ? "Roll" : _label.Trim());

            var result = Engine.RollAsync(State, UserService.CurrentUser, request);
            if (result.TryGetSuccess(out _))
            {
                await OnClose.InvokeAsync();
            }
            else if (result.TryGetFailure(out var err) && Toasts is not null)
            {
                await Toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            }
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
