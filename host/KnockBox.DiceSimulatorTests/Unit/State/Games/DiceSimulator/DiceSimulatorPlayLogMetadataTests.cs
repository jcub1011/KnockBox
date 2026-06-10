using KnockBox.DiceSimulator.Services.State.Games;
using KnockBox.DiceSimulator.Services.State.Games.Data;
using KnockBox.DiceSimulator.Services.State.Games.PlayLog;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.DiceSimulator.Tests.Unit.State
{
    [TestClass]
    public class DiceSimulatorPlayLogMetadataTests
    {
        private Mock<ILogger<DiceSimulatorGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<DiceSimulatorGameState>>();
            _host = UserFactory.Create("HostUser", System.Guid.NewGuid());
        }

        private static DiceRollEntry CreateEntry(System.Guid playerId)
        {
            return new DiceRollEntry
            {
                Id = System.Guid.NewGuid(),
                PlayerId = playerId,
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
        public void Build_ReportsTotalRollsMyRollsAndPlayers()
        {
            using var state = new DiceSimulatorGameState(_host, _loggerMock.Object);

            var me = System.Guid.NewGuid();
            var other = System.Guid.NewGuid();

            // Two rolls for me, one for another player => 3 total.
            state.AddRoll(CreateEntry(me));
            state.AddRoll(CreateEntry(me));
            state.AddRoll(CreateEntry(other));

            state.GetOrAddPlayerStats(me, "Me");
            state.GetOrAddPlayerStats(other, "Other");

            var metadata = DiceSimulatorPlayLogMetadata.Build(state, me);

            Assert.AreEqual("3", metadata["Total Rolls"]);
            Assert.AreEqual("2", metadata["My Rolls"]);
            Assert.AreEqual("2", metadata["Players"]);
        }

        [TestMethod]
        public void Build_OmitsMyRolls_WhenCurrentUserUnknown()
        {
            using var state = new DiceSimulatorGameState(_host, _loggerMock.Object);
            state.AddRoll(CreateEntry(System.Guid.NewGuid()));

            var metadata = DiceSimulatorPlayLogMetadata.Build(state, currentUserId: null);

            Assert.AreEqual("1", metadata["Total Rolls"]);
            Assert.IsFalse(metadata.ContainsKey("My Rolls"));
        }
    }
}
