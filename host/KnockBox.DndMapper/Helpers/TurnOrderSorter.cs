using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Sorts initiative entries per §9.5.2: descending by roll, then players-
    /// before-NPCs, then alphabetical by name. Pure / deterministic.
    /// </summary>
    public static class TurnOrderSorter
    {
        public static List<CombatantEntry> Sort(IEnumerable<CombatantEntry> entries)
            => [.. entries
                .OrderByDescending(e => e.InitiativeRoll ?? int.MinValue)
                .ThenBy(e => e.OwnerUserId is null ? 1 : 0)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

        /// <summary>
        /// Returns the index at which <paramref name="entry"/> should be inserted
        /// into <paramref name="ordered"/> so that the list remains sorted under
        /// the §9.5.2 tiebreak.
        /// </summary>
        public static int FindInsertionIndex(IReadOnlyList<CombatantEntry> ordered, CombatantEntry entry)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (Compare(entry, ordered[i]) < 0) return i;
            }
            return ordered.Count;
        }

        private static int Compare(CombatantEntry a, CombatantEntry b)
        {
            int ra = a.InitiativeRoll ?? int.MinValue;
            int rb = b.InitiativeRoll ?? int.MinValue;
            if (ra != rb) return rb.CompareTo(ra); // desc
            int pa = a.OwnerUserId is null ? 1 : 0;
            int pb = b.OwnerUserId is null ? 1 : 0;
            if (pa != pb) return pa.CompareTo(pb);
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
