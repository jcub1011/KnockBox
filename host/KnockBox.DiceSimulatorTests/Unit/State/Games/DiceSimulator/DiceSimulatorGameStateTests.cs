using KnockBox.DiceSimulator.Services.State.Games;
using KnockBox.DiceSimulator.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.DiceSimulator.Tests.Unit.State
{
    [TestClass]
    public class DiceSimulatorGameStateTests
    {
        private Mock<ILogger<DiceSimulatorGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<DiceSimulatorGameState>>();
            _host = UserFactory.Create("HostUser", System.Guid.NewGuid());
        }

        private DiceRollEntry CreateValidEntry()
        {
            return new DiceRollEntry
            {
                Id = System.Guid.NewGuid(),
                PlayerId = System.Guid.NewGuid(),
                PlayerName = "Player",
                DiceType = DiceType.D20,
                DiceCount = 1,
                Modifier = 0,
                Mode = RollMode.Normal,
                Result = 15,
                RawRolls = new int[] { 15 },
                AltRolls = null,
                AltTotal = 0,
                Expression = "1d20",
                Timestamp = System.DateTimeOffset.UtcNow
            };
        }

        [TestMethod]
        public void AddRoll_AddsRollToHistory()
        {
            using var state = new DiceSimulatorGameState(_host, _loggerMock.Object);
            var entry = CreateValidEntry();

            state.AddRoll(entry);

            Assert.HasCount(1, state.RollHistory);
            Assert.AreEqual(entry.Id, state.RollHistory[0].Id);
        }

        [TestMethod]
        public void GetOrAddPlayerStats_ReturnsNewOrExistingStat()
        {
            using var state = new DiceSimulatorGameState(_host, _loggerMock.Object);

            var playerId = System.Guid.NewGuid();
            var stats1 = state.GetOrAddPlayerStats(playerId, "Player One");
            Assert.AreEqual("Player One", stats1.PlayerName);

            stats1.TotalRolls = 5;

            var stats2 = state.GetOrAddPlayerStats(playerId, "Player One");
            Assert.AreEqual(5, stats2.TotalRolls);
            Assert.AreSame(stats1, stats2);
        }

        [TestMethod]
        public void ClearHistory_RemovesAllRollsAndStats()
        {
            using var state = new DiceSimulatorGameState(_host, _loggerMock.Object);
            state.AddRoll(CreateValidEntry());
            state.GetOrAddPlayerStats(System.Guid.NewGuid(), "Player One");

            Assert.HasCount(1, state.RollHistory);
            Assert.HasCount(1, state.PlayerStats);

            state.ClearHistory();

            Assert.IsEmpty(state.RollHistory);
            Assert.IsEmpty(state.PlayerStats);
        }
    }
}
