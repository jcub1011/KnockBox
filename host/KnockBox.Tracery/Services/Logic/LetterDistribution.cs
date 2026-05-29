using System.Collections.Frozen;
using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.Tracery.Services.Logic
{
    /// <summary>
    /// The weighted letter source for board generation (GDD §6). A single curated,
    /// auditable frequency table plus a one-call weighted draw — kept in one place so
    /// it stays playtest-tunable (GDD §10) and so generation (M03) and scoring (M06)
    /// share the same definition of which letters are "rare".
    /// </summary>
    /// <remarks>
    /// The weights are Boggle-die derived, not raw English-text frequency or Scrabble
    /// tile counts. For a tracing game what matters is vowel/consonant <em>balance</em>
    /// so most random boards stay word-rich on the first try: text frequency over-weights
    /// e/t and starves vowel variety, while Scrabble counts are tuned for rack economics,
    /// not adjacency. The table sums to <see cref="Total"/> (100); vowels carry ~41 of it.
    /// Bias is toward common, highly-combinable letters; q/v/w/x/z/j/k stay rare-but-present
    /// so the rare-letter bonus has something to fire on without flooding the board with
    /// dead tiles.
    /// </remarks>
    internal static class LetterDistribution
    {
        // Indexed by (letter - 'a'). Sums to Total. Edit here to retune; Total recomputes.
        //                                   a  b  c  d   e  f  g  h  i  j  k  l  m  n  o  p  q  r  s  t  u  v  w  x  y  z
        private static readonly int[] Weights = { 9, 2, 3, 4, 12, 2, 3, 4, 8, 1, 1, 4, 3, 6, 8, 2, 1, 6, 5, 6, 4, 1, 1, 1, 2, 1 };

        private static readonly int Total = Sum(Weights);

        /// <summary>
        /// Letters treated as "rare" — the single source of truth shared by the
        /// generator's rare-letter quality gate (M03) and rare-letter scoring (M06),
        /// so the two can never drift apart. These are the high-value Scrabble tiles
        /// (J/Q/Z and K/V/W/X/Y), case-folded on lookup via <see cref="IsRare"/>.
        /// </summary>
        internal static readonly FrozenSet<char> RareLetters =
            new[] { 'j', 'k', 'q', 'v', 'w', 'x', 'y', 'z' }.ToFrozenSet();

        /// <summary>True if <paramref name="c"/> is a rare letter (case-insensitive).</summary>
        internal static bool IsRare(char c) => RareLetters.Contains(char.ToLowerInvariant(c));

        /// <summary>
        /// Draws one lowercase letter weighted by the table. Consumes exactly one
        /// <see cref="IRandomNumberService.GetRandomInt(int, RandomType)"/> call, so
        /// deterministic RNG doubles can predict the draw sequence. Lowercase because
        /// the dictionary is lowercase ASCII and the solver preserves grid casing.
        /// </summary>
        internal static char Next(IRandomNumberService rng)
        {
            int roll = rng.GetRandomInt(Total); // [0, Total)
            int acc = 0;
            for (int i = 0; i < Weights.Length; i++)
            {
                acc += Weights[i];
                if (roll < acc) return (char)('a' + i);
            }
            return 'a'; // unreachable while Weights sum to Total; defensive only.
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            foreach (int v in values) total += v;
            return total;
        }
    }
}
