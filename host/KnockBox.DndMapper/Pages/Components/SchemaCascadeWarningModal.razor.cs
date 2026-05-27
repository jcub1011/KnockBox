using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class SchemaCascadeWarningModal : ComponentBase
    {
        [Parameter] public bool IsOpen { get; set; }
        [Parameter] public EventCallback OnConfirm { get; set; }
        [Parameter] public EventCallback OnCancel { get; set; }

        // Optional context the host can pass through. When both are supplied
        // the message includes counts of effect templates that will be hidden
        // and active-effect deltas that will silently no-op after the swap.
        [Parameter] public DndMapperGameState? State { get; set; }
        [Parameter] public AttributeSchema? PendingSchema { get; set; }

        private const string BaseMessage =
            "Changing the attribute schema mid-session will rebuild every existing character sheet. " +
            "Attributes whose name + type match the new schema keep their value. Attributes that don't " +
            "match are reset to defaults. Attributes not in the new schema are removed. This cannot be undone.";

        private string BuildMessage()
        {
            if (State is null || PendingSchema is null) return BaseMessage;

            var hiddenTemplates = State.GetActiveSchemaTemplate()?.StatusEffectTemplates.Length ?? 0;

            var incoming = new HashSet<string>(PendingSchema.Rows.Select(r => r.Name), StringComparer.Ordinal);
            int orphanDeltas = 0;
            foreach (var sheet in State.Sheets.Values)
            {
                foreach (var effect in sheet.StatusEffects)
                {
                    foreach (var d in effect.AttributeDeltas)
                    {
                        if (!incoming.Contains(d.AttributeName)) orphanDeltas++;
                    }
                }
            }

            if (hiddenTemplates == 0 && orphanDeltas == 0) return BaseMessage;

            var parts = new List<string>();
            if (hiddenTemplates > 0)
            {
                parts.Add($"{hiddenTemplates} status-effect template{(hiddenTemplates == 1 ? "" : "s")} authored under the current schema will be hidden from the Effect Library while the new schema is active (they reappear if you switch back).");
            }
            if (orphanDeltas > 0)
            {
                parts.Add($"{orphanDeltas} active effect delta{(orphanDeltas == 1 ? "" : "s")} on sheets reference attribute names the new schema doesn't define and will silently no-op until you switch back.");
            }

            return BaseMessage + "\n\n" + string.Join("\n\n", parts);
        }
    }
}
