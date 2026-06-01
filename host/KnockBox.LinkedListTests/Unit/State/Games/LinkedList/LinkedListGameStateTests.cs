using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.LinkedList.Tests.Unit.State
{
    [TestClass]
    public class LinkedListGameStateTests
    {
        private Mock<ILogger<LinkedListGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<LinkedListGameState>>();
            _host = UserFactory.Create("HostUser", "host-id");
        }

        [TestMethod]
        public void Constructor_SetsHost()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            Assert.AreSame(_host, state.Host);
        }

        [TestMethod]
        public void SetJoinable_TogglesJoinableState()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            state.Execute(() => state.SetJoinable(true));
            Assert.IsTrue(state.IsJoinable);

            state.Execute(() => state.SetJoinable(false));
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public void DefaultSettings_HaveExpectedValues()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            Assert.AreEqual(ScoringMode.FewestGuesses, state.Settings.ScoringMode);
            Assert.AreEqual(PlayerStructure.Collective, state.Settings.PlayerStructure);
            Assert.AreEqual(3, state.Settings.RejectionCap);
            Assert.IsFalse(state.Settings.NoImmediateRepeat);
            Assert.IsFalse(state.Settings.HostPlays);
            Assert.IsNull(state.Settings.Par);
            Assert.IsTrue(state.Settings.EnableTimers);
        }

        [TestMethod]
        public void DefaultPhase_IsSetup()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
        }

        [TestMethod]
        public void SetPhase_ChangesPhase()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            state.Execute(() => state.SetPhase(LinkedListGamePhase.Playing));

            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
        }

        [TestMethod]
        public void UpdateSettings_ReplacesSettingsAtomically()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);

            var result = state.UpdateSettings(s => s with
            {
                ScoringMode = ScoringMode.FastestTime,
                RejectionCap = 0,
                Par = 12,
            });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ScoringMode.FastestTime, state.Settings.ScoringMode);
            Assert.AreEqual(0, state.Settings.RejectionCap);
            Assert.AreEqual(12, state.Settings.Par);
        }

        [TestMethod]
        public void UpdateSettings_ReflectsHostPlaysIntoHostIsParticipant()
        {
            using var state = new LinkedListGameState(_host, _loggerMock.Object);
            Assert.IsFalse(state.HostIsParticipant);

            state.UpdateSettings(s => s with { HostPlays = true });
            Assert.IsTrue(state.HostIsParticipant);

            state.UpdateSettings(s => s with { HostPlays = false });
            Assert.IsFalse(state.HostIsParticipant);
        }
    }
}
