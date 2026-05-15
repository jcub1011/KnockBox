using System.Text.RegularExpressions;
using KnockBox.DndMapper.Services.Logic.Games;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class DefaultColorPaletteTests
    {
        private static readonly Regex HexColor = new("^#[0-9A-F]{6}$", RegexOptions.Compiled);

        [TestMethod]
        public void FromName_IsDeterministic()
        {
            Assert.AreEqual(DefaultColorPalette.FromName("Alice"), DefaultColorPalette.FromName("Alice"));
            Assert.AreEqual(DefaultColorPalette.FromName("Goblin"), DefaultColorPalette.FromName("Goblin"));
        }

        [TestMethod]
        public void FromName_ReturnsValidSixDigitHex()
        {
            foreach (var name in new[] { "A", "Alice", "Bob", "Goblin", "Some Long Name 12345" })
            {
                var hex = DefaultColorPalette.FromName(name);
                Assert.IsTrue(HexColor.IsMatch(hex), $"'{hex}' for name '{name}' is not a valid 6-digit hex.");
            }
        }

        [TestMethod]
        public void FromName_NullOrWhitespace_ReturnsNeutral()
        {
            Assert.AreEqual(DefaultColorPalette.Neutral, DefaultColorPalette.FromName(null));
            Assert.AreEqual(DefaultColorPalette.Neutral, DefaultColorPalette.FromName(""));
            Assert.AreEqual(DefaultColorPalette.Neutral, DefaultColorPalette.FromName("   "));
        }

        [TestMethod]
        public void FromName_DifferentNames_ProduceDistinctColorsForSampleSet()
        {
            var sample = new[] { "Alice", "Bob", "Goblin", "Bandit", "Drake" };
            var colors = sample.Select(DefaultColorPalette.FromName).Distinct().Count();
            // Probabilistic but the sample is tiny and the hash space is 360 hues —
            // require at least 4 distinct colors out of 5 names.
            Assert.IsTrue(colors >= 4, $"Expected ≥4 distinct colors, got {colors}.");
        }

        [TestMethod]
        public void FromName_IgnoresSurroundingWhitespace()
        {
            Assert.AreEqual(DefaultColorPalette.FromName("Alice"), DefaultColorPalette.FromName("  Alice  "));
        }
    }
}
