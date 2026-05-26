using System.Collections.Generic;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    // Predicate the initiative panels / projector chip use to decide whether
    // to reveal a CombatantEntry.InitiativeRoll. We treat the value as
    // "spoiled" while DiceCanvas still has a roll animating for the same
    // combatant, so the number drops in only once the dice settle — matching
    // the hide-until-settled behaviour RollLogPanel already gives the
    // roll log.
    //
    // DiceCanvas.OnStateChangedAsync marks new rolls animating in its
    // synchronous prelude (before the awaited InvokeAsync), so by the time
    // sibling subscribers render for the same StateChanged notification
    // every newly-appended roll is already in the tracker — no extra
    // ordering work needed at the call sites.
    public static class InitiativeAnimationGate
    {
        public static bool IsAnimatingFor(
            IReadOnlyList<RollResult> rollLog,
            IDiceAnimationTracker tracker,
            CombatantEntry entry)
        {
            if (rollLog is null || tracker is null || entry is null) return false;

            // Walk from the tail: the relevant roll is almost always one of
            // the most-recently-appended entries (it was just emitted in the
            // notification we're rendering for), so this hits in O(1) in
            // practice.
            for (int i = rollLog.Count - 1; i >= 0; i--)
            {
                var r = rollLog[i];
                if (!IsInitiativeRollForEntry(r, entry)) continue;
                if (tracker.IsAnimating(r.Id)) return true;
            }
            return false;
        }

        // Bulk / manual NPC initiative rolls carry TokenId so DiceCanvas can
        // key per-NPC DiceBox instances; player initiative rolls
        // (Submit / Force) have a null TokenId and live under the
        // user-keyed DiceBox instead. Both flows tag their RollResult with
        // Label = "Initiative" via BuildInitiativeRollResult — the label
        // guard keeps a player's mid-combat attack roll from getting
        // confused with their initiative.
        private static bool IsInitiativeRollForEntry(RollResult r, CombatantEntry entry)
        {
            if (r.TokenId is { } rollTokenId)
            {
                return rollTokenId == entry.TokenId;
            }
            return entry.OwnerUserId is { } owner
                && r.RollerUserId == owner
                && r.Label == "Initiative";
        }
    }
}
