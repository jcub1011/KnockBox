using System.Collections.Immutable;
using KnockBox.Tracery.Models;

namespace KnockBox.Tracery.Services.Logic
{
    /// <summary>
    /// Pure, deterministic scoring for Tracery (GDD §5). Every layer's constants come from
    /// <see cref="TracerySettings"/> so playtests can retune them (GDD §10) without touching this
    /// code. All methods are static and side-effect free — mirroring Spardle's
    /// <c>PointsForSolver</c> — so they can be unit-tested in isolation; the round-close pass in
    /// <c>TraceryGameEngine.CompleteRound</c> resolves unique-find across all banks and calls
    /// <see cref="Score"/> per word.
    /// </summary>
    public static class TraceryScorer
    {
        /// <summary>Base score — the word's length (GDD §5.1).</summary>
        public static int BaseScore(string word) => word?.Length ?? 0;

        /// <summary>
        /// Superlinear length bonus (GDD §5.2). Reads <paramref name="table"/> indexed by length;
        /// lengths at or beyond the last index clamp to that final ("10+") entry, and lengths
        /// below the table are zero.
        /// </summary>
        public static int LengthBonus(int length, ImmutableArray<int> table)
        {
            if (table.IsDefaultOrEmpty || length <= 0) return 0;
            int index = Math.Min(length, table.Length - 1);
            return table[index];
        }

        /// <summary>
        /// Rare-letter bonus (GDD §5.3): the sum of <paramref name="table"/>'s value for each
        /// qualifying letter occurrence (repeats count). Case-insensitive — letters are matched
        /// against the table's upper-case keys.
        /// </summary>
        public static int RareLetterBonus(string word, IReadOnlyDictionary<char, int> table)
        {
            if (string.IsNullOrEmpty(word) || table is null || table.Count == 0) return 0;

            int sum = 0;
            foreach (char c in word)
                if (table.TryGetValue(char.ToUpperInvariant(c), out int bonus))
                    sum += bonus;
            return sum;
        }

        /// <summary>
        /// Final points for a single word (GDD §5.4): the rare-letter and unique-find layers honor
        /// their <see cref="TracerySettings"/> toggles. The unique-find multiplier applies to the
        /// <em>whole</em> word score so harder unique finds pay more, and the product is rounded
        /// half-away-from-zero so the proposed ×1.5 lands on the GDD's worked totals.
        /// </summary>
        public static int WordScore(string word, bool isUnique, TracerySettings settings)
            => Score(word, isUnique, settings).Points;

        /// <summary>
        /// Computes the full per-word breakdown the reveal renders, including the final
        /// <see cref="TraceryWordScore.Points"/>. <see cref="WordScore"/> is the thin convenience
        /// over this when only the total is needed.
        /// </summary>
        public static TraceryWordScore Score(string word, bool isUnique, TracerySettings settings)
        {
            int baseScore = BaseScore(word);
            int lengthBonus = LengthBonus(baseScore, settings.LengthBonusTable);
            int rareLetterBonus = settings.RareLetterBonusEnabled
                ? RareLetterBonus(word, settings.RareLetterBonusTable)
                : 0;

            int subtotal = baseScore + lengthBonus + rareLetterBonus;
            double multiplier = isUnique && settings.UniqueFindBonusEnabled
                ? settings.UniqueFindMultiplier
                : 1.0;
            int points = (int)Math.Round(subtotal * multiplier, MidpointRounding.AwayFromZero);

            return new TraceryWordScore
            {
                Word = word ?? string.Empty,
                BaseScore = baseScore,
                LengthBonus = lengthBonus,
                RareLetterBonus = rareLetterBonus,
                IsUnique = isUnique,
                Points = points
            };
        }
    }
}
