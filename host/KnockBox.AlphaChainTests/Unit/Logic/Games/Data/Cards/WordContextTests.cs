using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.Data.Cards
{
    /// <summary>
    /// Unit tests for <see cref="WordContext"/>'s vowel/consonant detection. Vowels and consonants
    /// are no longer cached counts — they are computed on the fly via <see cref="WordContext.IsVowel"/>
    /// and <see cref="WordContext.IsConsonant"/>, which lets The Catalyst make Y/W/H count as vowels.
    /// </summary>
    [TestClass]
    public class WordContextTests
    {
        private static WordContext Plain(string word) => WordContext.Build(word, null);
        private static WordContext Catalyst(string word) =>
            WordContext.Build(word, null, 0, 12, 1.0, catalyst: true);

        [TestMethod]
        public void IsVowel_TrueForAeiou_FalseForPlainConsonants()
        {
            var ctx = Plain("anything");
            foreach (char v in "aeiou")
                Assert.IsTrue(ctx.IsVowel(v), $"'{v}' is always a vowel.");
            foreach (char c in "bcdfg")
                Assert.IsFalse(ctx.IsVowel(c), $"'{c}' is not a vowel.");
        }

        [TestMethod]
        public void IsVowel_YWH_AreConsonantsWithoutCatalyst()
        {
            var ctx = Plain("why");
            foreach (char c in "ywh")
                Assert.IsFalse(ctx.IsVowel(c), $"'{c}' is a plain consonant without The Catalyst.");
        }

        [TestMethod]
        public void IsVowel_YWH_AreVowelsWithCatalyst()
        {
            var ctx = Catalyst("why");
            foreach (char c in "ywh")
                Assert.IsTrue(ctx.IsVowel(c), $"The Catalyst makes '{c}' count as a vowel.");
        }

        [TestMethod]
        public void IsConsonant_TrueForAllNonVowels_RegardlessOfCatalyst()
        {
            // Consonant detection deliberately ignores The Catalyst: Y/W/H count as BOTH, so they
            // stay consonants even when the catalyst also makes them vowels.
            foreach (var ctx in new[] { Plain("why"), Catalyst("why") })
            {
                foreach (char c in "bcdywh")
                    Assert.IsTrue(ctx.IsConsonant(c), $"'{c}' is a consonant ({(ctx.UseCatalystRules ? "catalyst" : "plain")}).");
                foreach (char v in "aeiou")
                    Assert.IsFalse(ctx.IsConsonant(v), $"'{v}' is never a consonant.");
            }
        }

        [TestMethod]
        public void Counts_OverMixedWord_PlainVsCatalyst()
        {
            // "way" → plain: vowel 'a' (1), consonants w,y (2). Catalyst: w,a,y all vowels (3),
            // consonants w,y unchanged (2) — the ambiguous letters add to both counts.
            var plain = Plain("way");
            Assert.AreEqual(1, plain.Word.Count(plain.IsVowel));
            Assert.AreEqual(2, plain.Word.Count(plain.IsConsonant));

            var catalyst = Catalyst("way");
            Assert.AreEqual(3, catalyst.Word.Count(catalyst.IsVowel));
            Assert.AreEqual(2, catalyst.Word.Count(catalyst.IsConsonant));
        }
    }
}
