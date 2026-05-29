using System.Collections.Immutable;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    /// <summary>
    /// Covers <see cref="TraceryScorer"/> (Milestone 06): each scoring layer in isolation against
    /// the GDD §5 tables, the unique-find multiplier + rounding rule, and the §5.5 worked example.
    /// Mirrors Spardle's directly-unit-tested <c>PointsForSolver</c>.
    /// </summary>
    [TestClass]
    public class TraceryScorerTests
    {
        // The default settings encode the GDD's starting tables, so they double as the
        // golden reference for these table-driven assertions.
        private static readonly TracerySettings Default = new();

        // ── Base score (GDD §5.1) ───────────────────────────────────────────

        [TestMethod]
        [DataRow("rate", 4)]
        [DataRow("quartz", 6)]
        [DataRow("", 0)]
        public void BaseScore_IsWordLength(string word, int expected)
            => Assert.AreEqual(expected, TraceryScorer.BaseScore(word));

        // ── Length bonus (GDD §5.2) ─────────────────────────────────────────

        [TestMethod]
        [DataRow(4, 0)]
        [DataRow(5, 1)]
        [DataRow(6, 3)]
        [DataRow(7, 6)]
        [DataRow(8, 10)]
        [DataRow(9, 15)]
        [DataRow(10, 21)]
        public void LengthBonus_MatchesTheGddTable(int length, int expected)
            => Assert.AreEqual(expected, TraceryScorer.LengthBonus(length, Default.LengthBonusTable));

        [TestMethod]
        [DataRow(11)]
        [DataRow(16)]
        public void LengthBonus_ClampsToTheLastEntry_ForOversizedWords(int length)
            => Assert.AreEqual(21, TraceryScorer.LengthBonus(length, Default.LengthBonusTable));

        [TestMethod]
        public void LengthBonus_BelowMinimumOrEmptyTable_IsZero()
        {
            Assert.AreEqual(0, TraceryScorer.LengthBonus(3, Default.LengthBonusTable));
            Assert.AreEqual(0, TraceryScorer.LengthBonus(0, Default.LengthBonusTable));
            Assert.AreEqual(0, TraceryScorer.LengthBonus(6, ImmutableArray<int>.Empty));
        }

        // ── Rare-letter bonus (GDD §5.3) ────────────────────────────────────

        [TestMethod]
        [DataRow("rate", 0)]          // no rare letters
        [DataRow("milky", 2)]         // K(+1) + Y(+1)
        [DataRow("jinx", 6)]          // J(+3) + X(+3)
        [DataRow("quartz", 10)]       // Q(+5) + Z(+5)
        [DataRow("buzz", 10)]         // Z(+5) + Z(+5): repeats count
        public void RareLetterBonus_SumsPerQualifyingOccurrence(string word, int expected)
            => Assert.AreEqual(expected, TraceryScorer.RareLetterBonus(word, Default.RareLetterBonusTable));

        [TestMethod]
        public void RareLetterBonus_IsCaseInsensitive()
            => Assert.AreEqual(10, TraceryScorer.RareLetterBonus("QUARTZ", Default.RareLetterBonusTable));

        // ── Word score: multiplier + rounding (GDD §5.4) ────────────────────

        [TestMethod]
        public void WordScore_NonUnique_AppliesNoMultiplier()
        {
            // "rate": base 4 + length 0 + rare 0 = 4, no multiplier.
            Assert.AreEqual(4, TraceryScorer.WordScore("rate", isUnique: false, Default));
        }

        [TestMethod]
        public void WordScore_Unique_AppliesMultiplierToTheWholeScore()
        {
            // "table": base 5 + length 1 + rare 0 = 6; ×1.5 = 9.
            Assert.AreEqual(9, TraceryScorer.WordScore("table", isUnique: true, Default));
        }

        [TestMethod]
        public void WordScore_RoundsHalfAwayFromZero()
        {
            // Zero out the length bonus so we can land an exact .5 product:
            // "wade" → base 4 + length 0 + rare W(+1) = 5; ×1.5 = 7.5 → 8 (half away from zero).
            var settings = Default with { LengthBonusTable = [0, 0, 0, 0, 0] };
            Assert.AreEqual(8, TraceryScorer.WordScore("wade", isUnique: true, settings));
        }

        [TestMethod]
        public void WordScore_UniqueBonusDisabled_WithholdsMultiplier()
        {
            var settings = Default with { UniqueFindBonusEnabled = false };
            // "table" subtotal 6, but no multiplier because the toggle is off.
            Assert.AreEqual(6, TraceryScorer.WordScore("table", isUnique: true, settings));
        }

        [TestMethod]
        public void WordScore_RareLetterDisabled_WithholdsThatLayer()
        {
            var settings = Default with { RareLetterBonusEnabled = false };
            // "quartz": base 6 + length 3 + rare 0 (disabled) = 9, non-unique.
            Assert.AreEqual(9, TraceryScorer.WordScore("quartz", isUnique: false, settings));
        }

        // ── Worked example (GDD §5.5) ───────────────────────────────────────

        [TestMethod]
        public void Score_Quartz_Unique_MatchesTheWorkedExample()
        {
            var breakdown = TraceryScorer.Score("quartz", isUnique: true, Default);

            Assert.AreEqual(6, breakdown.BaseScore);        // length
            Assert.AreEqual(3, breakdown.LengthBonus);      // §5.2 row for 6
            Assert.AreEqual(10, breakdown.RareLetterBonus); // Q(+5) + Z(+5)
            Assert.IsTrue(breakdown.IsUnique);
            // (6 + 3 + 10) × 1.5 = 28.5 → 29 (half away from zero).
            Assert.AreEqual(29, breakdown.Points);
        }
    }
}
