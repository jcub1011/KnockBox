using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Logic.Games;

namespace KnockBox.DndMapperTests.Unit.Helpers
{
    [TestClass]
    public class TokenTextContrastTests
    {
        [TestMethod]
        public void TextFillFor_LightBackground_ReturnsBlack()
        {
            Assert.AreEqual("#000000", TokenTextContrast.TextFillFor("#ffffff"));
            Assert.AreEqual("#000000", TokenTextContrast.TextFillFor("#ffff00")); // yellow
            Assert.AreEqual("#000000", TokenTextContrast.TextFillFor("#80ff80")); // light green
        }

        [TestMethod]
        public void TextFillFor_DarkBackground_ReturnsWhite()
        {
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("#000000"));
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("#1f77b4")); // mid-blue (old palette[0])
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("#d62728")); // mid-red
        }

        [TestMethod]
        public void TextFillFor_NullOrEmpty_ReturnsWhite()
        {
            // FillFor returns "#444" (dark) for these tokens, so white text is correct.
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor(null));
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor(""));
        }

        [TestMethod]
        public void TextFillFor_ShortHex_IsExpandedAndScored()
        {
            // #fff expands to #ffffff (light → black text).
            Assert.AreEqual("#000000", TokenTextContrast.TextFillFor("#fff"));
            // #000 expands to #000000 (dark → white text).
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("#000"));
        }

        [TestMethod]
        public void TextFillFor_MalformedHex_FallsBackToWhite()
        {
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("not-a-color"));
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("#xyzxyz"));
            Assert.AreEqual("#ffffff", TokenTextContrast.TextFillFor("#1234"));
        }

        [TestMethod]
        public void TextFillFor_NameDerivedDefaults_AreReadable()
        {
            // Every FromName output should pick *some* contrasting text color
            // (never returns empty/unparseable). Smoke check across a sample.
            foreach (var name in new[] { "Alice", "Bob", "Goblin", "Bandit", "Drake", "X", "Z" })
            {
                var bg = DefaultColorPalette.FromName(name);
                var text = TokenTextContrast.TextFillFor(bg);
                Assert.IsTrue(text == "#000000" || text == "#ffffff",
                    $"Unexpected text fill '{text}' for background '{bg}' (name '{name}').");
            }
        }
    }
}
