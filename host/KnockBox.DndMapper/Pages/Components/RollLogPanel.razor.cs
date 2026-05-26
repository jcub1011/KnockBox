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
    public partial class RollLogPanel : DisposableComponent
    {
        // Visible window is intentionally small so the panel fits comfortably in
        // the side rail without crowding the character sheet. Older rolls remain
        // in state up to DndMapperGameState.RollLogCap and are reachable via the
        // "See All Rolls" history modal.
        private const int MaxEntries = 6;
        private static readonly int[] DieSizes = [4, 6, 8, 10, 12, 20, 100];

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

        private bool _historyOpen;
        private List<RollResult> _historySnapshot = [];
        private DateTime _historyCapturedAt;

        private List<CharacterSheet> _pickableSheets = [];

        private bool _libraryOpen;

        // Host always sees the indicator when a rule fired; players see it
        // only when LoadedDicePlayerIndicator is RedDotInLog. Master toggle
        // off ⇒ never shown (and AppliedRules will be empty anyway).
        private bool ShouldShowLoadedIndicator(RollResult r)
        {
            if (r.AppliedRules.Length == 0) return false;
            if (IsHost) return true;
            return State.Settings.LoadedDicePlayerIndicator == LoadedDicePlayerIndicator.RedDotInLog;
        }

        // Lists fired rules so the host's hover tooltip can call out which
        // ones bent this roll. Truncated to 4 names for layout safety.
        private static string LoadedTooltip(RollResult r)
        {
            const int max = 4;
            var names = r.AppliedRules.Select(x => string.IsNullOrEmpty(x.RuleName) ? "(unnamed)" : x.RuleName).Take(max);
            var joined = string.Join(", ", names);
            return r.AppliedRules.Length > max
                ? $"Loaded dice: {joined}, +{r.AppliedRules.Length - max} more"
                : $"Loaded dice: {joined}";
        }

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            _trackerSub = () => _ = InvokeAsync(StateHasChanged);
            Tracker.Changed += _trackerSub;
            ResolvePickableSheets();
            base.OnInitialized();
        }

        protected override void OnParametersSet()
        {
            ResolvePickableSheets();
            base.OnParametersSet();
        }

        private List<RollResult> Visible
        {
            get
            {
                var filtered = RollLogVisibilityFilter.VisibleTo(
                    State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
                var list = filtered.Where(r => !Tracker.IsAnimating(r.Id)).ToList();
                if (list.Count > MaxEntries)
                {
                    list = list.GetRange(list.Count - MaxEntries, MaxEntries);
                }
                list.Reverse();
                return list;
            }
        }

        private static string FormatTimestamp(DateTime utc) =>
            utc.ToLocalTime().ToString("HH:mm:ss");

        // d100 is rolled visually as a percentile pair (tens die 00-90 + units
        // die 0-9) because dice-box-threejs has no 100-face geometry. The chip
        // still shows the single 1-100 result; the tooltip explains the pair.
        private static string DieTooltip(DieRoll d)
        {
            if (d.Sides == 100)
            {
                int tens = (d.Result == 100) ? 0 : (d.Result / 10) * 10;
                int units = (d.Result == 100) ? 0 : (d.Result % 10);
                return $"d100 (percentile dice: {tens:D2} + {units} = {d.Result})";
            }
            return $"d{d.Sides}";
        }

        private bool CanQuickRoll
        {
            get
            {
                int total = Config.Terms.Sum(t => Math.Max(0, t.Count));
                return total >= 1 && total <= 20;
            }
        }

        private string QuickRollLabel => BuildFormula(includeAdvDis: true);

        private string QuickRollTitle => CanQuickRoll
            ? $"{BuildFormula(includeAdvDis: true)} — click to roll"
            : "Configure dice in the Configuration section first";

        private string BuildFormula(bool includeAdvDis)
        {
            var dice = string.Join("+",
                Config.Terms.Where(t => t.Count > 0).Select(t => $"{t.Count}d{t.Sides}"));
            if (string.IsNullOrEmpty(dice)) return "—";

            string attr = (Config.PickerSheetId is not null && !string.IsNullOrEmpty(Config.AttributeName))
                ? $" +{Config.AttributeName}"
                : string.Empty;

            string flat = Config.FlatModifier == 0 ? string.Empty
                : (Config.FlatModifier > 0 ? $" +{Config.FlatModifier}" : $" {Config.FlatModifier}");

            string mode = !includeAdvDis ? string.Empty
                : Config.Mode == RollMode.Advantage ? " (ADV)"
                : Config.Mode == RollMode.Disadvantage ? " (DIS)"
                : string.Empty;

            return $"{dice}{attr}{flat}{mode}";
        }

        private string RollerName(string rollerUserId)
        {
            if (State.Host.Id == rollerUserId) return State.Host.Name;
            var entry = State.Players.FirstOrDefault(p => p.User.Id == rollerUserId);
            return entry.User is null ? "?" : entry.DisplayName;
        }

        private Task QuickRoll() =>
            DiceRollSubmitter.SubmitAsync(Engine, State, UserService.CurrentUser, Config, Toasts);

        // Repeat a past roll with the same dice, attribute binding, flat
        // modifier and label. Shift/Ctrl held while clicking overrides the
        // mode for the new roll (Adv/Dis only takes effect when the request
        // is a single die; otherwise the engine would reject it and the
        // submitter coerces back to Normal anyway).
        private Task ReRoll(RollResult r, MouseEventArgs e)
        {
            if (!CanReRoll(r)) return Task.CompletedTask;

            bool singleDie = r.OriginalDice.Length == 1 && r.OriginalDice[0].Count == 1;
            RollMode mode = r.Mode;
            if (singleDie)
            {
                if (e.ShiftKey && !e.CtrlKey) mode = RollMode.Advantage;
                else if (e.CtrlKey && !e.ShiftKey) mode = RollMode.Disadvantage;
            }

            var request = new RollRequest(
                Dice: [.. r.OriginalDice],
                AttributeRef: r.OriginalAttributeRef,
                FlatModifier: r.FlatModifier,
                Mode: mode,
                Label: string.IsNullOrWhiteSpace(r.Label) ? "Roll" : r.Label);

            return DiceRollSubmitter.SubmitRequestAsync(Engine, State, UserService.CurrentUser, request, Toasts);
        }

        // Visible only on rolls the current user authored. NPC rolls (TokenId
        // set) are deferred — the host's "re-roll NPC X" path is a follow-up.
        // OriginalDice == empty means the record was persisted before the
        // re-roll feature shipped, so we can't faithfully repeat it.
        private bool CanReRoll(RollResult r) =>
            r.TokenId is null
            && !string.IsNullOrEmpty(CurrentUserId)
            && r.RollerUserId == CurrentUserId
            && r.OriginalDice.Length > 0;

        private static string ReRollTitle(RollResult r) =>
            $"Re-roll {r.Formula}{(string.IsNullOrEmpty(r.Label) ? "" : $" — {r.Label}")}. "
            + "Hold Shift for Advantage · Ctrl for Disadvantage.";

        private void OpenHistory()
        {
            var filtered = RollLogVisibilityFilter.VisibleTo(
                State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
            var list = filtered.Where(r => !Tracker.IsAnimating(r.Id)).ToList();
            list.Reverse();
            _historySnapshot = list;
            _historyCapturedAt = DateTime.Now;
            _historyOpen = true;
        }

        private void CloseHistory()
        {
            _historyOpen = false;
            _historySnapshot = [];
        }

        // ── Dice configuration (folded in from the former DiceRollerModal) ──

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

            // Host can intentionally pick "No sheet (GM)" — keep it null
            // unless a previously-picked sheet has been deleted, in which
            // case fall back to null (GM) rather than silently switching
            // to an unrelated sheet. Players don't have the No-sheet
            // option, so we auto-lock them to their assigned sheet.
            if (IsHost)
            {
                if (Config.PickerSheetId is Guid id && !_pickableSheets.Any(s => s.Id == id))
                    Config.PickerSheetId = null;
            }
            else if (Config.PickerSheetId is null || !_pickableSheets.Any(s => s.Id == Config.PickerSheetId))
            {
                var fallback = _pickableSheets.FirstOrDefault(s => s.OwnerUserId == CurrentUserId)
                            ?? _pickableSheets.FirstOrDefault();
                Config.PickerSheetId = fallback?.Id;
            }
        }

        private bool HasAssignedSheet =>
            !IsHost && State.Sheets.Values.Any(s => s.OwnerUserId == CurrentUserId);

        // The sheet whose per-sheet roll templates the panel should surface
        // and the library modal should author against. Players are pinned to
        // their assigned sheet; the host follows the From Sheet picker.
        private CharacterSheet? ActiveSheetForTemplates =>
            IsHost
                ? PickerSheet
                : State.Sheets.Values.FirstOrDefault(s => s.OwnerUserId == CurrentUserId);

        private IEnumerable<RollTemplate> VisibleRollTemplates
        {
            get
            {
                foreach (var t in DndMapperGameState.BuiltInRollTemplates) yield return t;
                foreach (var t in State.GlobalRollTemplates) yield return t;
                var sheet = ActiveSheetForTemplates;
                if (sheet is not null)
                {
                    foreach (var t in sheet.RollTemplates) yield return t;
                }
            }
        }

        private static string ScopeClass(RollTemplateScope scope) => scope switch
        {
            RollTemplateScope.BuiltIn => "dndm-chip--builtin",
            RollTemplateScope.Global => "dndm-chip--global",
            RollTemplateScope.Sheet => "dndm-chip--sheet",
            _ => string.Empty,
        };

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

        // Applies a template's configured dice / flat / mode / label / attribute
        // into the shared Config.
        //
        // Attribute resolution rules:
        // - Template carries a name AND the schema has it → bind it.
        // - Template carries a name but the schema doesn't have it → leave the
        //   user's current selection in place. (Earlier this branch silently
        //   nulled, which produced a "looks selected but isn't" bug where the
        //   dropdown displayed an attribute the engine had no way to bind to.)
        // - Template carries no name (baseline d20 / 2d6 / etc.) → leave the
        //   user's current selection in place. A bare die-roll preset
        //   shouldn't clobber an attribute the user already picked.
        private void ApplyTemplate(RollTemplate t)
        {
            Config.Terms = [.. t.Dice];
            Config.FlatModifier = t.FlatModifier;
            Config.Mode = t.Mode;
            Config.Label = t.Label ?? string.Empty;

            if (!string.IsNullOrEmpty(t.AttributeName)
                && State.AttributeSchema.Rows.Any(r => r.Name == t.AttributeName))
            {
                Config.AttributeName = t.AttributeName;
            }
            // Both fallthrough branches intentionally leave Config.AttributeName.
        }

        private void OpenTemplateLibrary() => _libraryOpen = true;
        private void CloseTemplateLibrary() => _libraryOpen = false;

        private int TotalDiceCount => Config.Terms.Sum(t => Math.Max(0, t.Count));
        private bool CanAdvDis => Config.Terms.Count == 1 && Config.Terms[0].Count == 1;

        private static string PillCls(bool active) => active ? "active" : string.Empty;

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i : fallback;
        }

        private void AddTerm() => Config.Terms.Add(new DiceTerm(1, 20));

        // The Adv/Dis pills are visually disabled when !CanAdvDis (see the
        // razor), but Config.Mode is intentionally preserved across temporary
        // invalid configurations: a user who set Adv then bumps to 2d6 and
        // back to 1d20 should keep their Adv selection. Submission paths
        // coerce Mode → Normal when the dice config can't support Adv/Dis
        // (see DiceRollSubmitter), so the engine never sees an inconsistent
        // request.
        private void RemoveTerm(int idx)
        {
            if (idx >= 0 && idx < Config.Terms.Count) Config.Terms.RemoveAt(idx);
            if (Config.Terms.Count == 0) Config.Terms.Add(new DiceTerm(1, 20));
        }

        private void UpdateTermCount(int idx, int count)
        {
            count = Math.Clamp(count, 0, 20);
            Config.Terms[idx] = Config.Terms[idx] with { Count = count };
        }

        private void UpdateTermSides(int idx, int sides)
        {
            Config.Terms[idx] = Config.Terms[idx] with { Sides = sides };
        }

        private void OnAttributeChange(string? raw)
        {
            Config.AttributeName = string.IsNullOrEmpty(raw) ? null : raw;
        }

        private void OnSheetChange(string? raw)
        {
            // Empty value ⇒ the "No sheet (GM)" option. Clear any picked
            // attribute too — the attribute dropdown only lists rows from
            // the picker sheet, so a stale AttributeName would dangle
            // without a sheet to resolve it against.
            if (string.IsNullOrEmpty(raw))
            {
                Config.PickerSheetId = null;
                Config.AttributeName = null;
                return;
            }
            if (Guid.TryParse(raw, out var id) && _pickableSheets.Any(s => s.Id == id))
            {
                Config.PickerSheetId = id;
            }
        }

        private void OnLabelChange(string? raw) => Config.Label = raw ?? string.Empty;

        private void OnFlatModChange(object? raw) => Config.FlatModifier = ParseInt(raw, Config.FlatModifier);

        private void SetMode(RollMode mode) => Config.Mode = mode;

        public override void Dispose()
        {
            _stateSub?.Dispose();
            if (_trackerSub is not null) Tracker.Changed -= _trackerSub;
            base.Dispose();
        }
    }
}
