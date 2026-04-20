using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDress.Tests.Unit.Logic.Games.DrawnToDress
{
    [TestClass]
    public class EntrantIdParsingTests
    {
        // ── TryParse ────────────────────────────────────────────────────────

        [TestMethod]
        public void TryParse_ValidFormat_ReturnsTrueWithParsedValues()
        {
            var result = EntrantId.TryParse("player1:1", out var entrantId);

            Assert.IsTrue(result);
            Assert.AreEqual("player1", entrantId.PlayerId);
            Assert.AreEqual(1, entrantId.Round);
        }

        [TestMethod]
        public void TryParse_ComplexPlayerId_ReturnsTrueWithParsedValues()
        {
            var result = EntrantId.TryParse("abc-def:3", out var entrantId);

            Assert.IsTrue(result);
            Assert.AreEqual("abc-def", entrantId.PlayerId);
            Assert.AreEqual(3, entrantId.Round);
        }

        [TestMethod]
        public void TryParse_MissingColon_ReturnsFalse()
        {
            var result = EntrantId.TryParse("player1", out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParse_EmptyString_ReturnsFalse()
        {
            var result = EntrantId.TryParse("", out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParse_Null_ReturnsFalse()
        {
            var result = EntrantId.TryParse(null, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParse_NoRoundPart_ReturnsFalse()
        {
            var result = EntrantId.TryParse("player1:", out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParse_NonNumericRound_ReturnsFalse()
        {
            var result = EntrantId.TryParse("player1:abc", out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParse_ColonAtStart_ReturnsFalse()
        {
            var result = EntrantId.TryParse(":1", out _);

            Assert.IsFalse(result);
        }

        // ── Properties ──────────────────────────────────────────────────────

        [TestMethod]
        public void PlayerId_AfterValidParse_ReturnsPlayerId()
        {
            EntrantId.TryParse("player1:2", out var entrantId);

            Assert.AreEqual("player1", entrantId.PlayerId);
        }

        [TestMethod]
        public void Round_AfterValidParse_ReturnsRound()
        {
            EntrantId.TryParse("player1:2", out var entrantId);

            Assert.AreEqual(2, entrantId.Round);
        }

        // ── ToString ────────────────────────────────────────────────────────

        [TestMethod]
        public void ToString_ReturnsCanonicalFormat()
        {
            var entrantId = new EntrantId("player1", 2);

            Assert.AreEqual("player1:2", entrantId.ToString());
        }

        [TestMethod]
        public void ToString_AfterParse_RoundTrips()
        {
            EntrantId.TryParse("abc-def:3", out var entrantId);

            Assert.AreEqual("abc-def:3", entrantId.ToString());
        }

        // ── Value Equality ──────────────────────────────────────────────────

        [TestMethod]
        public void Equals_SamePlayerIdAndRound_AreEqual()
        {
            var a = new EntrantId("player1", 1);
            var b = new EntrantId("player1", 1);

            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void Equals_DifferentPlayerId_AreNotEqual()
        {
            var a = new EntrantId("player1", 1);
            var b = new EntrantId("player2", 1);

            Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void Equals_DifferentRound_AreNotEqual()
        {
            var a = new EntrantId("player1", 1);
            var b = new EntrantId("player1", 2);

            Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void Equals_ParsedAndConstructed_AreEqual()
        {
            EntrantId.TryParse("player1:1", out var parsed);
            var constructed = new EntrantId("player1", 1);

            Assert.AreEqual(parsed, constructed);
        }

        // ── GetOutfitByEntrantId ─────────────────────────────────────────────

        [TestMethod]
        public async Task GetOutfitByEntrantId_NonExistentEntrant_ReturnsNull()
        {
            // Arrange: create a real game context via the engine.
            var engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            var stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            var randomMock = new Mock<IRandomNumberService>();
            randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);

            var host = new User("Host", "host1");
            var engine = new DrawnToDressGameEngine(
                engineLoggerMock.Object,
                stateLoggerMock.Object,
                randomMock.Object);

            var stateResult = await engine.CreateStateAsync(host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            // Act
            var outfit = context.GetOutfitByEntrantId(new EntrantId("nonexistent", 1));

            // Assert
            Assert.IsNull(outfit);
        }
    }
}
