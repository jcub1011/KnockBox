using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Scoring
{
    /// <summary>
    /// Unit tests for the deterministic scoring pipeline <c>Score = (L + ΣA) × ΠM</c>,
    /// realised as a left → right walk of the Engine Bay seeded with the word length.
    /// </summary>
    [TestClass]
    public class ScoreCalculatorTests
    {
        private readonly ScoreCalculator _calc = new();

        // Build a context for a word with an explicit vowel/consonant split. The word text
        // only matters for cards that read ctx.Word (e.g. Letter Hoarder); the counts are
        // taken from the real letters when we build via WordContext.Build.
        private static WordContext Ctx(string word, char? banned = null) =>
            WordContext.Build(word, banned);

        // A trivial additive card of fixed value.
        private static ModifierCard Additive(double value) =>
            new("add", "Add", "", ModifierKind.Additive, _ => true, _ => value);

        // A trivial multiplicative card of fixed factor.
        private static ModifierCard Mult(double factor) =>
            new("mul", "Mul", "", ModifierKind.Multiplicative, _ => true, _ => factor);

        [TestMethod]
        public void EmptyBay_ScoreEqualsLength()
        {
            var ctx = Ctx("cat"); // length 3
            Assert.AreEqual(3, _calc.Calculate(ctx, []));
        }

        [TestMethod]
        public void SingleAdditive_AddsToLength()
        {
            var ctx = Ctx("cat"); // length 3
            Assert.AreEqual(3 + 10, _calc.Calculate(ctx, [Additive(10)]));
        }

        [TestMethod]
        public void SingleMultiplicative_MultipliesLength()
        {
            var ctx = Ctx("cats"); // length 4
            Assert.AreEqual(4 * 3, _calc.Calculate(ctx, [Mult(3)]));
        }

        [TestMethod]
        public void TwoAdditivesThenMultiplicative_StacksThenExplodes()
        {
            var ctx = Ctx("cat"); // length 3
            // (3 + 2 + 5) × 2 = 20
            var bay = new[] { Additive(2), Additive(5), Mult(2) };
            Assert.AreEqual(20, _calc.Calculate(ctx, bay));
        }

        [TestMethod]
        public void MultiplicativeBeforeAdditive_RespectsLeftToRightPipeline()
        {
            var ctx = Ctx("cat"); // length 3
            // Suboptimal order: (3 × 2) + 5 = 11, NOT (3 + 5) × 2 = 16.
            var bay = new[] { Mult(2), Additive(5) };
            Assert.AreEqual(11, _calc.Calculate(ctx, bay));
        }

        [TestMethod]
        public void ConditionalMiss_CardIgnored()
        {
            var ctx = Ctx("cat"); // length 3
            // Trigger never fires → contributes nothing; score is just the length.
            var never = new ModifierCard("x", "X", "", ModifierKind.Multiplicative, _ => false, _ => 100);
            Assert.AreEqual(3, _calc.Calculate(ctx, [never]));
        }

        [TestMethod]
        public void RoundsHalfUpAtTheEnd()
        {
            var ctx = Ctx("cat"); // length 3
            // 3 × 1.5 = 4.5 → rounds half-up (away from zero) to 5.
            Assert.AreEqual(5, _calc.Calculate(ctx, [Mult(1.5)]));
        }

        [TestMethod]
        public void CapsAtMaxWordScore()
        {
            var ctx = Ctx("cat");
            // 3 × 100000 well exceeds the cap.
            Assert.AreEqual(ScoreCalculator.MaxWordScore, _calc.Calculate(ctx, [Mult(100_000)]));
        }

        // ── Vowel Surge specific cases (library card) ───────────────────────

        private static ModifierCard VowelSurge =>
            ModifierLibrary.FindById("vowel-surge")!;

        [TestMethod]
        public void VowelSurge_FiresWhenVowelsExceedConsonants()
        {
            // "aerie": a,e,i,e vowels (4) vs r (1) consonant → vowels > consonants → ×2.
            var ctx = Ctx("aerie"); // length 5
            Assert.AreEqual(5 * 2, _calc.Calculate(ctx, [VowelSurge]));
        }

        [TestMethod]
        public void VowelSurge_SkippedWhenConsonantsDominate()
        {
            // "crypt": 0 vowels, 5 consonants → trigger false → score == length.
            var ctx = Ctx("crypt"); // length 5
            Assert.AreEqual(5, _calc.Calculate(ctx, [VowelSurge]));
        }

        [TestMethod]
        public void TaxCollector_FiresOnlyWhenBannedLetterPresent()
        {
            var taxCollector = ModifierLibrary.FindById("tax-collector")!;

            // Banned 'a' present in "cat" → ×1.5 → 3 × 1.5 = 4.5 → 5.
            var withBanned = Ctx("cat", banned: 'a');
            Assert.AreEqual(5, _calc.Calculate(withBanned, [taxCollector]));

            // Banned 'z' absent from "cat" → trigger false → score == length.
            var withoutBanned = Ctx("cat", banned: 'z');
            Assert.AreEqual(3, _calc.Calculate(withoutBanned, [taxCollector]));
        }
    }
}
