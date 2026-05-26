using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
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
        [Inject] protected IDiceAnimationTracker Tracker { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private Action? _trackerSub;
        private bool _attrsExpanded;
        private bool _settingsOpen;
        private bool _presetsOpen;
        private bool _logOpen;
        private bool _historyOpen;

        // Sticky inline custom-dice selection for the preset popup. Not a saved
        // template — just the last [qty][sides] the player picked this circuit.
        private static readonly int[] DieSizes = [4, 6, 8, 10, 12, 20, 100];
        private int _customCount = 1;
        private int _customSides = 20;

        protected override void OnInitialized()
        {
            // Sheet values / status effects change the attribute modifiers we
            // render — re-render so the button numbers stay live.
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            // Animations settling move rolls into the visible window — re-render
            // the recent-rolls panel as they finish.
            _trackerSub = () => _ = InvokeAsync(StateHasChanged);
            Tracker.Changed += _trackerSub;
            base.OnInitialized();
        }

        // ── Recent-rolls quick panel ─────────────────────────────────────────
        // Mirrors RollLogPanel.Visible (same visibility gate + animation gate)
        // but capped at 5 and ordered newest-first for the floating peek.
        private const int RecentCap = 5;

        private List<RollResult> RecentRolls
        {
            get
            {
                var filtered = RollLogVisibilityFilter.VisibleTo(
                    State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
                var list = filtered.Where(r => !Tracker.IsAnimating(r.Id)).ToList();
                if (list.Count > RecentCap)
                {
                    list = list.GetRange(list.Count - RecentCap, RecentCap);
                }
                list.Reverse();
                return list;
            }
        }

        private void ToggleLog() => _logOpen = !_logOpen;

        private void OpenHistory() => _historyOpen = true;
        private void CloseHistory() => _historyOpen = false;

        // ── Dice-preset quick picker (gear popup) ────────────────────────────
        // The gear opens this lightweight picker rather than the full settings
        // modal. Selecting a template or the custom row applies *only* the dice
        // (quantity + sides) to the shared Config — attribute / flat / mode /
        // label are deliberately left untouched — so the main roll button just
        // changes which dice it throws. The Library button opens the full modal.
        private void TogglePresets() => _presetsOpen = !_presetsOpen;

        // Same cascade RollLogPanel.VisibleRollTemplates uses (built-in →
        // global → active sheet), keyed off this footer's ActiveSheet.
        private IEnumerable<RollTemplate> VisibleTemplates
        {
            get
            {
                foreach (var t in DndMapperGameState.BuiltInRollTemplates) yield return t;
                foreach (var t in State.GlobalRollTemplates) yield return t;
                var sheet = ActiveSheet;
                if (sheet is not null)
                {
                    foreach (var t in sheet.RollTemplates) yield return t;
                }
            }
        }

        private void ApplyDice(IReadOnlyList<DiceTerm> dice)
        {
            Config.Terms = [.. dice];
            _presetsOpen = false;
        }

        private void ApplyCustom()
        {
            Config.Terms = [new DiceTerm(_customCount, _customSides)];
            _presetsOpen = false;
        }

        private void OnCustomCount(object? raw) => _customCount = Math.Clamp(ParseInt(raw, _customCount), 0, 20);
        private void OnCustomSides(object? raw) => _customSides = ParseInt(raw, _customSides);

        // Flat modifier and source-sheet are live Config edits (they don't
        // close the popup): the popup mirrors the same two settings the full
        // settings modal exposes. Source-sheet is host-only (players are
        // pinned to their assigned sheet).
        private void OnFlatModChange(object? raw) => Config.FlatModifier = ParseInt(raw, Config.FlatModifier);

        private IEnumerable<CharacterSheet> PickableSheets =>
            State.Sheets.Values.OrderBy(s => s.CharacterName);

        private void OnSheetChange(string? raw)
        {
            // Empty value ⇒ "No sheet (GM)". Clear any picked attribute too so
            // a stale AttributeName doesn't dangle without a sheet to bind to.
            if (string.IsNullOrEmpty(raw))
            {
                Config.PickerSheetId = null;
                Config.AttributeName = null;
                return;
            }
            if (Guid.TryParse(raw, out var id) && State.Sheets.ContainsKey(id))
            {
                Config.PickerSheetId = id;
            }
        }

        // Mirrors RollLogPanel.TemplateTooltip: render the template's configured
        // dice + attribute + flat + mode as a compact formula for the row title.
        private static string TemplateTooltip(RollTemplate t)
        {
            var dice = string.Join("+", t.Dice.Select(d => $"{d.Count}d{d.Sides}"));
            var attr = string.IsNullOrEmpty(t.AttributeName) ? string.Empty : $" +{t.AttributeName}";
            var flat = t.FlatModifier == 0
                ? string.Empty
                : (t.FlatModifier > 0 ? $" +{t.FlatModifier}" : $" {t.FlatModifier}");
            var mode = t.Mode switch
            {
                RollMode.Advantage => " (ADV)",
                RollMode.Disadvantage => " (DIS)",
                _ => string.Empty,
            };
            return $"{dice}{attr}{flat}{mode}";
        }

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
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
        private void OpenSettings()
        {
            _presetsOpen = false;
            _settingsOpen = true;
        }
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
            if (_trackerSub is not null) Tracker.Changed -= _trackerSub;
            base.Dispose();
        }
    }
}
