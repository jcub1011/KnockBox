using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;

namespace KnockBox.AlphaChain.Tests.Unit
{
    /// <summary>
    /// Guards the coupling between <see cref="ModifierId"/> and the external icon sprite. CardIcon.razor
    /// emits <c>&lt;use href="...#{ModifierId.ToString()}"&gt;</c>, so every non-<see cref="ModifierId.Unknown"/>
    /// member must have a matching <c>&lt;symbol id&gt;</c> in cards.svg or that card silently falls back.
    /// </summary>
    [TestClass]
    public class CardIconSpriteTests
    {
        private static readonly HashSet<string> SymbolIds = ParseSymbolIds();

        [TestMethod]
        public void EveryModifierId_ExceptUnknown_HasMatchingSymbol()
        {
            var missing = Enum.GetValues<ModifierId>()
                .Where(id => id != ModifierId.Unknown)
                .Select(id => id.ToString())
                .Where(name => !SymbolIds.Contains(name))
                .ToList();

            Assert.AreEqual(
                0,
                missing.Count,
                $"cards.svg is missing <symbol> ids for: {string.Join(", ", missing)}");
        }

        [TestMethod]
        public void Sprite_DefinesFallbackSymbol()
        {
            Assert.IsTrue(
                SymbolIds.Contains("fallback"),
                "cards.svg must define a 'fallback' symbol for Unknown / unmapped ids.");
        }

        private static HashSet<string> ParseSymbolIds([CallerFilePath] string testFilePath = "")
        {
            // testFilePath: host/KnockBox.AlphaChainTests/Unit/CardIconSpriteTests.cs
            var testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(testFilePath))!;
            var spritePath = Path.Combine(
                testProjectDir, "..", "KnockBox.AlphaChain", "wwwroot", "icons", "cards.svg");

            Assert.IsTrue(File.Exists(spritePath), $"Sprite not found at {spritePath}");

            var svg = File.ReadAllText(spritePath);
            return Regex.Matches(svg, "<symbol\\s+id=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();
        }
    }
}
