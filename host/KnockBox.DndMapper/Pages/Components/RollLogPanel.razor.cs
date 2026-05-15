using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class RollLogPanel : DisposableComponent
    {
        // Visible window is intentionally small so the panel fits comfortably in
        // the side rail without crowding the character sheet. Older rolls remain
        // in state up to DndMapperGameState.RollLogCap and would be visible if
        // we ever surface a "show full log" affordance.
        private const int MaxEntries = 6;

        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter, EditorRequired] public DiceRollerConfig Config { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }
        [Parameter] public EventCallback OnOpenSettings { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        // Snapshot of the full visible roll history, captured when the user
        // opens the history modal. Intentionally NOT refreshed on subsequent
        // state changes so the user can scroll a stable list while new rolls
        // come in; they reopen to see updates.
        private bool _historyOpen;
        private List<RollResult> _historySnapshot = [];
        private DateTime _historyCapturedAt;

        private List<RollResult> Visible
        {
            get
            {
                var filtered = RollLogVisibilityFilter.VisibleTo(
                    State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
                var list = filtered.ToList();
                // Show newest first. Take the last MaxEntries from the stored
                // (chronological) log, then reverse so the most recent is on top.
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

        private bool CanQuickRoll
        {
            get
            {
                int total = Config.Terms.Sum(t => Math.Max(0, t.Count));
                return total >= 1 && total <= 20;
            }
        }

        // Short formula used as the button label, e.g. "1d20 +DEX (ADV)".
        // Kept compact so it doesn't overflow the panel header.
        private string QuickRollLabel => BuildFormula(includeAdvDis: true);

        // Longer tooltip — same formula plus a hint about clicking ⚙ for the
        // full dice editor. The label already covers the dice math, so we
        // don't prefix with "Roll:".
        private string QuickRollTitle => CanQuickRoll
            ? $"{BuildFormula(includeAdvDis: true)} — click to roll"
            : "Configure dice in settings (⚙) first";

        private string BuildFormula(bool includeAdvDis)
        {
            var dice = string.Join("+",
                Config.Terms.Where(t => t.Count > 0).Select(t => $"{t.Count}d{t.Sides}"));
            if (string.IsNullOrEmpty(dice)) return "—";

            // Attribute modifier is only meaningful when a sheet is selected;
            // otherwise the engine ignores it.
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

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        private string RollerName(string rollerUserId)
        {
            if (State.Host.Id == rollerUserId) return State.Host.Name;
            var entry = State.Players.FirstOrDefault(p => p.User.Id == rollerUserId);
            return entry.User is null ? "?" : entry.DisplayName;
        }

        private Task QuickRoll() =>
            DiceRollSubmitter.SubmitAsync(Engine, State, UserService.CurrentUser, Config, Toasts);

        private Task OpenSettings() => OnOpenSettings.InvokeAsync();

        private void OpenHistory()
        {
            // Snapshot the full visibility-filtered log (newest first), capped
            // implicitly by RollLogCap on the engine side. The snapshot is a
            // shallow copy of the references — RollResult is immutable, so
            // there's nothing further to clone.
            var filtered = RollLogVisibilityFilter.VisibleTo(
                State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
            var list = filtered.ToList();
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

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }
    }
}
