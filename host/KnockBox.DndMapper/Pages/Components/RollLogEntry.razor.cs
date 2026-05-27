using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KnockBox.DndMapper.Pages.Components
{
    // A single roll-log entry. Extracted from RollLogPanel so the footer's
    // recent-rolls quick panel renders byte-identical entries (scoped CSS is
    // per-component, so shared styling requires a shared component).
    public partial class RollLogEntry : DisposableComponent
    {
        [Parameter, EditorRequired] public RollResult Roll { get; set; } = default!;
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public string CurrentUserId { get; set; } = string.Empty;
        [Parameter] public bool IsHost { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

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

        private string RollerName(string rollerUserId)
        {
            if (State.Host.Id == rollerUserId) return State.Host.Name;
            var entry = State.Players.FirstOrDefault(p => p.User.Id == rollerUserId);
            return entry.User is null ? "?" : entry.DisplayName;
        }

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
    }
}
