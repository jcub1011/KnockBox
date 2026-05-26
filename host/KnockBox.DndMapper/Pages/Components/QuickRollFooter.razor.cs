using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KnockBox.DndMapper.Pages.Components
{
    // Bottom footer bar that mirrors the canvas toolbar. The common case —
    // "roll my configured dice" and "roll an attribute check" — is one click;
    // Shift/Ctrl held at click time overrides to Advantage/Disadvantage. The
    // gear opens the full RollLogPanel (config + log) in a modal.
    //
    // The footer never mutates Config.Mode: the modifier-key choice is a
    // per-click override applied to the submitted RollRequest only. The sticky
    // default lives on the mode pills inside the settings modal.
    public partial class QuickRollFooter : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public DiceRollerConfig Config { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private bool _attrsExpanded;
        private bool _settingsOpen;

        protected override void OnInitialized()
        {
            // Sheet values / status effects change the attribute modifiers we
            // render — re-render so the button numbers stay live.
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        // ── Active sheet ─────────────────────────────────────────────────────
        // Player: their assigned sheet. Host: the "roll as" selection made in
        // the settings modal (Config.PickerSheetId); null ⇒ GM, no attributes.
        private CharacterSheet? ActiveSheet
        {
            get
            {
                if (IsHost)
                {
                    if (Config.PickerSheetId is Guid id && State.Sheets.TryGetValue(id, out var s)) return s;
                    return null;
                }
                return State.Sheets.Values.FirstOrDefault(s => s.OwnerUserId == CurrentUserId);
            }
        }

        private IEnumerable<AttributeRow> NumericRows =>
            State.AttributeSchema.Rows.Where(r =>
                r.Type == AttributeValueType.Score || r.Type == AttributeValueType.Modifier);

        private bool ShowAttrArrow => ActiveSheet is not null && NumericRows.Any();

        // Live modifier shown on each attribute button — same resolution path
        // the engine uses, so the number always matches the rolled result.
        private int AttrModifier(AttributeRow row)
        {
            var sheet = ActiveSheet;
            if (sheet is null) return 0;
            var value = sheet.Values.TryGetValue(row.Name, out var v) ? v : row.Default;
            return AttributeContributionResolver.Resolve(sheet, row.Name, value).EffectiveModifier;
        }

        private void ToggleAttrs() => _attrsExpanded = !_attrsExpanded;

        // ── Main roll button (uses Config) ───────────────────────────────────
        private bool CanQuickRoll
        {
            get
            {
                int total = Config.Terms.Sum(t => Math.Max(0, t.Count));
                return total >= 1 && total <= 20;
            }
        }

        private string QuickRollLabel => BuildFormula();

        private string QuickRollTitle => CanQuickRoll
            ? $"{BuildFormula()} — click to roll. Hold Shift / Ctrl to override mode."
            : "Open settings (⚙) to configure dice first.";

        private string BuildFormula()
        {
            var dice = string.Join("+",
                Config.Terms.Where(t => t.Count > 0).Select(t => $"{t.Count}d{t.Sides}"));
            if (string.IsNullOrEmpty(dice)) return "—";

            string attr = (Config.PickerSheetId is not null && !string.IsNullOrEmpty(Config.AttributeName))
                ? $" +{Config.AttributeName}"
                : string.Empty;

            string flat = Config.FlatModifier == 0 ? string.Empty
                : (Config.FlatModifier > 0 ? $" +{Config.FlatModifier}" : $" {Config.FlatModifier}");

            string mode = Config.Mode == RollMode.Advantage ? " (ADV)"
                : Config.Mode == RollMode.Disadvantage ? " (DIS)"
                : string.Empty;

            return $"{dice}{attr}{flat}{mode}";
        }

        private Task QuickRoll(MouseEventArgs e)
        {
            RollMode? overrideMode = ModifierKeyMode(e);
            return DiceRollSubmitter.SubmitAsync(Engine, State, UserService.CurrentUser, Config, Toasts, overrideMode);
        }

        // ── Attribute buttons ────────────────────────────────────────────────
        // Roll the current config dice + flat modifier, with this attribute
        // appended (per the agreed behaviour). Mode = modifier-key override, or
        // the sticky Config.Mode, coerced to Normal when the dice aren't a
        // single die so the engine never rejects an Adv/Dis request.
        private string AttrTitle(AttributeRow row, int mod)
        {
            var formula = string.Join("+", Config.Terms.Where(t => t.Count > 0).Select(t => $"{t.Count}d{t.Sides}"));
            if (string.IsNullOrEmpty(formula)) formula = "—";
            return $"Roll {formula} +{row.Name} ({FormatSigned(mod)}). Hold Shift for Advantage · Ctrl for Disadvantage.";
        }

        private Task RollAttribute(AttributeRow row, MouseEventArgs e)
        {
            var sheet = ActiveSheet;
            if (sheet is null) return Task.CompletedTask;

            var dice = Config.Terms.Where(t => t.Count > 0).ToList();
            if (dice.Count == 0) return Task.CompletedTask;

            bool singleDie = dice.Count == 1 && dice[0].Count == 1;
            var mode = ModifierKeyMode(e) ?? Config.Mode;
            if (mode != RollMode.Normal && !singleDie) mode = RollMode.Normal;

            var request = new RollRequest(
                Dice: [.. dice],
                AttributeRef: new AttributeRef(sheet.Id, row.Name),
                FlatModifier: Config.FlatModifier,
                Mode: mode,
                Label: row.Name);

            return DiceRollSubmitter.SubmitRequestAsync(Engine, State, UserService.CurrentUser, request, Toasts);
        }

        // ── Settings modal ───────────────────────────────────────────────────
        private void OpenSettings() => _settingsOpen = true;
        private void CloseSettings() => _settingsOpen = false;

        // ── Helpers ──────────────────────────────────────────────────────────
        private static RollMode? ModifierKeyMode(MouseEventArgs e) =>
            e.ShiftKey && !e.CtrlKey ? RollMode.Advantage
            : e.CtrlKey && !e.ShiftKey ? RollMode.Disadvantage
            : null;

        private static string FormatSigned(int n) => n >= 0 ? $"+{n}" : n.ToString();

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
