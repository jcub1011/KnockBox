using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Pages.Components;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    internal static class DiceRollSubmitter
    {
        // Builds a RollRequest from the shared config and submits it through the
        // engine. Validation errors surface via the toast service. Returns true
        // on success so callers can choose to close a modal.
        public static Task<bool> SubmitAsync(
            DndMapperGameEngine engine,
            DndMapperGameState state,
            User? caller,
            DiceRollerConfig config,
            DndMapperToastService? toasts)
        {
            if (caller is null || config is null) return Task.FromResult(false);
            return SubmitAsync(engine, state, caller, config, toasts, modeOverride: null);
        }

        // Same as above but lets the caller override the mode for a single
        // submission without mutating Config.Mode — used by the quick-roll dock
        // when the user holds Shift/Ctrl while clicking the roll button. A
        // null override falls back to Config.Mode.
        public static async Task<bool> SubmitAsync(
            DndMapperGameEngine engine,
            DndMapperGameState state,
            User? caller,
            DiceRollerConfig config,
            DndMapperToastService? toasts,
            RollMode? modeOverride)
        {
            if (caller is null || config is null) return false;

            int total = config.Terms.Sum(t => Math.Max(0, t.Count));
            if (total < 1 || total > 20)
            {
                if (toasts is not null) await toasts.Push("Total dice must be 1–20.", DndMapperToastTone.Warning);
                return false;
            }

            // AttributeRef carries the picker sheet whether or not an
            // attribute is selected — so a host "rolling as Alice" with no
            // attribute still counts as Alice's roll for loaded-dice
            // matching. AttributeName is null when no attribute is picked;
            // the engine's modifier-resolution path skips on null name.
            // Players' pickers are auto-locked to their assigned sheet.
            AttributeRef? attrRef = config.PickerSheetId is Guid sheetId
                ? new AttributeRef(sheetId, string.IsNullOrEmpty(config.AttributeName) ? null : config.AttributeName)
                : null;

            var dice = config.Terms.Where(t => t.Count > 0).ToList();

            // Mode coercion: Adv/Dis is engine-rejected unless the request is
            // exactly 1d{N}. Rather than relying on the UI to keep Config.Mode
            // valid (which we used to do at the cost of silently clobbering
            // the user's selection), coerce here so the roll just happens.
            // The user's saved preference in Config.Mode stays intact for the
            // next 1d{N} roll.
            var requestedMode = modeOverride ?? config.Mode;
            if (requestedMode != RollMode.Normal && !IsSingleDieRoll(dice))
                requestedMode = RollMode.Normal;

            var request = new RollRequest(
                Dice: [.. dice],
                AttributeRef: attrRef,
                FlatModifier: config.FlatModifier,
                Mode: requestedMode,
                Label: string.IsNullOrWhiteSpace(config.Label) ? "Roll" : config.Label.Trim());

            return await SubmitRequestAsync(engine, state, caller, request, toasts);
        }

        // Submits a pre-built RollRequest. Used by the dock's attribute chips
        // and the log's re-roll button — both bypass DiceRollerConfig so they
        // don't mutate the user's sticky preferences.
        public static async Task<bool> SubmitRequestAsync(
            DndMapperGameEngine engine,
            DndMapperGameState state,
            User? caller,
            RollRequest request,
            DndMapperToastService? toasts)
        {
            if (caller is null || request is null) return false;

            var result = engine.RollAsync(state, caller, request);
            if (result.TryGetSuccess(out _)) return true;
            if (result.TryGetFailure(out var err) && toasts is not null)
                await toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            return false;
        }

        private static bool IsSingleDieRoll(IReadOnlyList<DiceTerm> dice) =>
            dice.Count == 1 && dice[0].Count == 1;
    }
}
