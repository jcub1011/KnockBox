using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Tests.Helpers;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    [TestClass]
    public class LetterDistributionTests
    {
        // The weight table (a..z), mirrored here so the empirical test has an expected
        // distribution to compare against. Sum = 100, so each weight is its own percentage.
        private static readonly int[] Weights =
            { 9, 2, 3, 4, 12, 2, 3, 4, 8, 1, 1, 4, 3, 6, 8, 2, 1, 6, 5, 6, 4, 1, 1, 1, 2, 1 };

        // ── Cumulative-walk math (deterministic) ────────────────────────────────

        [TestMethod]
        public void Next_DeterministicRolls_MapToExpectedBucketLetters()
        {
            // Each roll is hand-placed inside a letter's cumulative-weight band:
            //   a=[0,9) b=[9,11) c=[11,14) e=[18,30) j=[47,48) r=[73,79) t=[84,90) z=[99,100)
            var rng = new SequentialRng(0, 8, 9, 18, 47, 73, 84, 99);
            char[] drawn = { L(rng), L(rng), L(rng), L(rng), L(rng), L(rng), L(rng), L(rng) };

            CollectionAssert.AreEqual(
                new[] { 'a', 'a', 'b', 'e', 'j', 'r', 't', 'z' }, drawn);

            static char L(IRandomNumberService r) => LetterDistribution.Next(r);
        }

        // ── Never emits an off-table character ───────────────────────────────────

        [TestMethod]
        public void Next_NeverEmitsOffTableChar()
        {
            var rng = new RandomNumberService();
            for (int i = 0; i < 10_000; i++)
            {
                char c = LetterDistribution.Next(rng);
                Assert.IsTrue(c is >= 'a' and <= 'z', $"Drew off-table char '{c}'.");
            }
        }

        // ── Empirical frequency matches the weight table ─────────────────────────

        [TestMethod]
        public void Next_EmpiricalFrequencies_MatchTableWithinTolerance()
        {
            const int draws = 200_000;
            const double tolerance = 0.02; // absolute; comfortably above sampling noise at this N
            var rng = new RandomNumberService();

            var counts = new int[26];
            for (int i = 0; i < draws; i++)
                counts[LetterDistribution.Next(rng) - 'a']++;

            for (int i = 0; i < 26; i++)
            {
                double observed = (double)counts[i] / draws;
                double expected = (double)Weights[i] / 100.0;
                Assert.IsTrue(Math.Abs(observed - expected) <= tolerance,
                    $"Letter '{(char)('a' + i)}': observed {observed:F4}, expected {expected:F4}.");
            }
        }

        // ── Rare-letter set ──────────────────────────────────────────────────────

        [TestMethod]
        public void RareLetters_MatchTheScoringBonusTable_CaseInsensitive()
        {
            // The rare set must equal the keys of TracerySettings.RareLetterBonusTable so a word
            // that scores a rare-letter bonus is exactly one that satisfies the generator's gate.
            var scoringRareLetters = new TracerySettings().RareLetterBonusTable.Keys;
            foreach (char upper in scoringRareLetters)
            {
                char lower = char.ToLowerInvariant(upper);
                Assert.IsTrue(LetterDistribution.IsRare(lower), $"'{lower}' should be rare.");
                Assert.IsTrue(LetterDistribution.IsRare(upper), $"'{upper}' uppercase should be rare.");
            }

            foreach (char c in new[] { 'a', 'e', 'r', 's', 't', 'n' })
                Assert.IsFalse(LetterDistribution.IsRare(c), $"'{c}' should not be rare.");
        }
    }
}
