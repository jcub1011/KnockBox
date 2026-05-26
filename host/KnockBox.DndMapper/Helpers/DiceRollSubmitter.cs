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
        public static async Task<bool> SubmitAsync(
            DndMapperGameEngine engine,
            DndMapperGameState state,
            User? caller,
            DiceRollerConfig config,
            DndMapperToastService? toasts)
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

            var request = new RollRequest(
                Dice: [.. config.Terms.Where(t => t.Count > 0)],
                AttributeRef: attrRef,
                FlatModifier: config.FlatModifier,
                Mode: config.Mode,
                Label: string.IsNullOrWhiteSpace(config.Label) ? "Roll" : config.Label.Trim());

            var result = engine.RollAsync(state, caller, request);
            if (result.TryGetSuccess(out _)) return true;
            if (result.TryGetFailure(out var err) && toasts is not null)
                await toasts.Push(err.PublicMessage, DndMapperToastTone.Danger);
            return false;
        }
    }
}
