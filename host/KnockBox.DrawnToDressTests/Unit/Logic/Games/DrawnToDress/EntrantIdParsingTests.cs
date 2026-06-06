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
        private static readonly Guid _testPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid _testPlayerId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // ── TryParse ────────────────────────────────────────────────────────

        [TestMethod]
        public void TryParse_ValidFormat_ReturnsTrueWithParsedValues()
        {
            var input = $"{_testPlayerId}:1";
            var result = EntrantId.TryParse(input, out var entrantId);

            Assert.IsTrue(result);
            Assert.AreEqual(_testPlayerId, entrantId.PlayerId);
            Assert.AreEqual(1, entrantId.Round);
        }

        [TestMethod]
        public void TryParse_AnotherValidFormat_ReturnsTrueWithParsedValues()
        {
            var input = $"{_testPlayerId2}:3";
            var result = EntrantId.TryParse(input, out var entrantId);

            Assert.IsTrue(result);
            Assert.AreEqual(_testPlayerId2, entrantId.PlayerId);
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
            EntrantId.TryParse($"{_testPlayerId}:2", out var entrantId);

            Assert.AreEqual(_testPlayerId, entrantId.PlayerId);
        }

        [TestMethod]
        public void Round_AfterValidParse_ReturnsRound()
        {
            EntrantId.TryParse($"{_testPlayerId}:2", out var entrantId);

            Assert.AreEqual(2, entrantId.Round);
        }

        // ── ToString ────────────────────────────────────────────────────────

        [TestMethod]
        public void ToString_ReturnsCanonicalFormat()
        {
            var entrantId = new EntrantId(_testPlayerId, 2);

            Assert.AreEqual($"{_testPlayerId}:2", entrantId.ToString());
        }

        [TestMethod]
        public void ToString_AfterParse_RoundTrips()
        {
            var input = $"{_testPlayerId2}:3";
            EntrantId.TryParse(input, out var entrantId);

            Assert.AreEqual(input, entrantId.ToString());
        }

        // ── Value Equality ──────────────────────────────────────────────────

        [TestMethod]
        public void Equals_SamePlayerIdAndRound_AreEqual()
        {
            var a = new EntrantId(_testPlayerId, 1);
            var b = new EntrantId(_testPlayerId, 1);

            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void Equals_DifferentPlayerId_AreNotEqual()
        {
            var a = new EntrantId(_testPlayerId, 1);
            var b = new EntrantId(_testPlayerId2, 1);

            Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void Equals_DifferentRound_AreNotEqual()
        {
            var a = new EntrantId(_testPlayerId, 1);
            var b = new EntrantId(_testPlayerId, 2);

            Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void Equals_ParsedAndConstructed_AreEqual()
        {
            EntrantId.TryParse($"{_testPlayerId}:1", out var parsed);
            var constructed = new EntrantId(_testPlayerId, 1);

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

            var host = UserFactory.Create("Host", Guid.NewGuid());
            var engine = new DrawnToDressGameEngine(
                engineLoggerMock.Object,
                stateLoggerMock.Object,
                randomMock.Object);

            var stateResult = await engine.CreateStateAsync(host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            // Act
            var outfit = context.GetOutfitByEntrantId(new EntrantId(Guid.NewGuid(), 1));

            // Assert
            Assert.IsNull(outfit);
        }
    }
}
