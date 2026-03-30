using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress
{
    [TestClass]
    public class EntrantIdParsingTests
    {
        // ── TryParseEntrantId ────────────────────────────────────────────────

        [TestMethod]
        public void TryParseEntrantId_ValidFormat_ReturnsTrueWithParsedValues()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId("player1:1", out var playerId, out var round);

            Assert.IsTrue(result);
            Assert.AreEqual("player1", playerId);
            Assert.AreEqual(1, round);
        }

        [TestMethod]
        public void TryParseEntrantId_ComplexPlayerId_ReturnsTrueWithParsedValues()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId("abc-def:3", out var playerId, out var round);

            Assert.IsTrue(result);
            Assert.AreEqual("abc-def", playerId);
            Assert.AreEqual(3, round);
        }

        [TestMethod]
        public void TryParseEntrantId_MissingColon_ReturnsFalse()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId("player1", out _, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseEntrantId_EmptyString_ReturnsFalse()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId("", out _, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseEntrantId_Null_ReturnsFalse()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId(null!, out _, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseEntrantId_NoRoundPart_ReturnsFalse()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId("player1:", out _, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseEntrantId_NonNumericRound_ReturnsFalse()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId("player1:abc", out _, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseEntrantId_ColonAtStart_ReturnsFalse()
        {
            var result = DrawnToDressGameContext.TryParseEntrantId(":1", out _, out _);

            Assert.IsFalse(result);
        }

        // ── GetPlayerIdFromEntrantId ─────────────────────────────────────────

        [TestMethod]
        public void GetPlayerIdFromEntrantId_ValidInput_ReturnsPlayerId()
        {
            var playerId = DrawnToDressGameContext.GetPlayerIdFromEntrantId("player1:2");

            Assert.AreEqual("player1", playerId);
        }

        [TestMethod]
        public void GetPlayerIdFromEntrantId_MalformedInput_ReturnsWholeString()
        {
            var result = DrawnToDressGameContext.GetPlayerIdFromEntrantId("malformed");

            Assert.AreEqual("malformed", result);
        }

        // ── GetOutfitRoundFromEntrantId ──────────────────────────────────────

        [TestMethod]
        public void GetOutfitRoundFromEntrantId_ValidInput_ReturnsRound()
        {
            var round = DrawnToDressGameContext.GetOutfitRoundFromEntrantId("player1:2");

            Assert.AreEqual(2, round);
        }

        [TestMethod]
        public void GetOutfitRoundFromEntrantId_MalformedInput_ReturnsZero()
        {
            var round = DrawnToDressGameContext.GetOutfitRoundFromEntrantId("malformed");

            Assert.AreEqual(0, round);
        }

        // ── GetOutfitByEntrantId ─────────────────────────────────────────────

        [TestMethod]
        public async Task GetOutfitByEntrantId_MalformedEntrantId_ReturnsNull()
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
            var outfit = context.GetOutfitByEntrantId("malformed");

            // Assert
            Assert.IsNull(outfit);
        }
    }
}
