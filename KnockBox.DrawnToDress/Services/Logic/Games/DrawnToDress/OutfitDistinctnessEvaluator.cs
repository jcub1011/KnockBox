using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Provides helpers for evaluating whether an Outfit 2 submission is sufficiently
    /// distinct from all Outfit 1 submissions, as required by the GDD.
    ///
    /// Distinctness is measured by counting the number of clothing-type slots in which
    /// Outfit 2 selects the same item as an Outfit 1.  If the count reaches or exceeds
    /// the configured threshold the submission is considered a violation.
    /// </summary>
    public static class OutfitDistinctnessEvaluator
    {
        /// <summary>
        /// Counts the number of clothing-type slots in which <paramref name="outfit2"/>
        /// selects the exact same item as <paramref name="outfit1"/>.
        /// </summary>
        /// <param name="outfit1">The Outfit 1 submission to compare against.</param>
        /// <param name="outfit2">The Outfit 2 submission under evaluation.</param>
        /// <returns>
        /// The number of shared (typeId, itemId) pairs across the two outfits.
        /// </returns>
        public static int CountSharedItems(OutfitSubmission outfit1, OutfitSubmission outfit2)
        {
            int count = 0;
            foreach (var (typeId, itemId) in outfit2.SelectedItemsByType)
            {
                if (outfit1.SelectedItemsByType.TryGetValue(typeId, out var outfit1Item)
                    && outfit1Item == itemId)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="outfit2"/> violates the
        /// distinctness rule against <em>any</em> of the supplied Outfit 1 submissions.
        ///
        /// A violation occurs when the number of shared items between <paramref name="outfit2"/>
        /// and any single Outfit 1 is greater than or equal to <paramref name="threshold"/>.
        /// </summary>
        /// <param name="outfit2">The Outfit 2 submission to evaluate.</param>
        /// <param name="allOutfit1s">All players' Outfit 1 submissions (including the submitter's own).</param>
        /// <param name="threshold">
        /// The minimum number of shared items required for a violation.
        /// A value of <c>0</c> disables the check (always returns <see langword="false"/>).
        /// </param>
        public static bool ViolatesDistinctnessRule(
            OutfitSubmission outfit2,
            IEnumerable<OutfitSubmission> allOutfit1s,
            int threshold)
        {
            if (threshold <= 0) return false;
            return allOutfit1s.Any(o1 => CountSharedItems(o1, outfit2) >= threshold);
        }
    }
}
