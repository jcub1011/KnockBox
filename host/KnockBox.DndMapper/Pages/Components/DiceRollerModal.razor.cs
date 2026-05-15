using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
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
        [Parameter, EditorRequired] public DiceRollerConfig Config { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        private List<CharacterSheet> _pickableSheets = [];

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

        private CharacterSheet? PickerSheet
        {
            get
            {
                if (Config.PickerSheetId is Guid id && State.Sheets.TryGetValue(id, out var s)) return s;
                return null;
            }
        }

        private void ResolvePickableSheets()
        {
            _pickableSheets = IsHost
                ? [.. State.Sheets.Values.OrderBy(s => s.CharacterName)]
                : [.. State.Sheets.Values.Where(s => s.OwnerUserId == CurrentUserId).OrderBy(s => s.CharacterName)];

            if (Config.PickerSheetId is null || !_pickableSheets.Any(s => s.Id == Config.PickerSheetId))
            {
                var fallback = _pickableSheets.FirstOrDefault(s => s.OwnerUserId == CurrentUserId)
                            ?? _pickableSheets.FirstOrDefault();
                Config.PickerSheetId = fallback?.Id;
            }
        }

        private int TotalDiceCount => Config.Terms.Sum(t => Math.Max(0, t.Count));
        // Adv/Dis is enabled for any single-die roll, not just d20 — the engine
        // matches this rule and rolls a second die of the same size.
        private bool CanAdvDis => Config.Terms.Count == 1 && Config.Terms[0].Count == 1;
        private bool CanRollInitiative => PickerSheet is not null
            && State.AttributeSchema.Rows.Any(r => r.Name.Equals("DEX", StringComparison.OrdinalIgnoreCase));

        private bool CanSubmit => TotalDiceCount > 0 && TotalDiceCount <= 20;

        private static string PillCls(bool active) => active ? "active" : string.Empty;

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }

        private void AddTerm() => Config.Terms.Add(new DiceTerm(1, 20));

        private void RemoveTerm(int idx)
        {
            if (idx >= 0 && idx < Config.Terms.Count) Config.Terms.RemoveAt(idx);
            if (Config.Terms.Count == 0) Config.Terms.Add(new DiceTerm(1, 20));
            if (!CanAdvDis) Config.Mode = RollMode.Normal;
        }

        private void UpdateTermCount(int idx, int count)
        {
            count = Math.Clamp(count, 0, 20);
            Config.Terms[idx] = Config.Terms[idx] with { Count = count };
            if (!CanAdvDis) Config.Mode = RollMode.Normal;
        }

        private void UpdateTermSides(int idx, int sides)
        {
            Config.Terms[idx] = Config.Terms[idx] with { Sides = sides };
            if (!CanAdvDis) Config.Mode = RollMode.Normal;
        }

        private void OnAttributeChange(string? raw)
        {
            Config.AttributeName = string.IsNullOrEmpty(raw) ? null : raw;
        }

        private void OnSheetChange(string? raw)
        {
            if (Guid.TryParse(raw, out var id) && _pickableSheets.Any(s => s.Id == id))
            {
                Config.PickerSheetId = id;
            }
        }

        private void OnLabelChange(string? raw) => Config.Label = raw ?? string.Empty;

        private void OnFlatModChange(object? raw) => Config.FlatModifier = ParseInt(raw, Config.FlatModifier);

        private void SetMode(RollMode mode) => Config.Mode = mode;

        private async Task QuickRoll(int count, int sides, string label)
        {
            Config.Terms = [new DiceTerm(count, sides)];
            Config.AttributeName = null;
            Config.FlatModifier = 0;
            Config.Mode = RollMode.Normal;
            Config.Label = label;
            await Submit();
        }

        private async Task RollInitiative()
        {
            Config.Terms = [new DiceTerm(1, 20)];
            Config.AttributeName = "DEX";
            Config.FlatModifier = 0;
            Config.Mode = RollMode.Normal;
            Config.Label = "Initiative";
            await Submit();
        }

        private Task OnSubmit() => Submit();

        private async Task Submit()
        {
            var ok = await DiceRollSubmitter.SubmitAsync(Engine, State, UserService.CurrentUser, Config, Toasts);
            if (ok) await OnClose.InvokeAsync();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
