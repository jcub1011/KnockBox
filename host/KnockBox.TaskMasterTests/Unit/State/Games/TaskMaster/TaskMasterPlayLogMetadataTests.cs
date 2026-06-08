using KnockBox.Core.Services.State.Users;
using KnockBox.TaskMaster.Services.State.Games;
using KnockBox.TaskMaster.Services.State.Games.PlayLog;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.TaskMaster.Tests.Unit.State
{
    [TestClass]
    public class TaskMasterPlayLogMetadataTests
    {
        private Mock<ILogger<TaskMasterGameState>> _loggerMock = default!;
        private User _host = default!;
        private TaskMasterGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<TaskMasterGameState>>();
            _host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new TaskMasterGameState(_host, _loggerMock.Object);
            _state.Execute(() => _state.SetJoinable(true));
        }

        private User AddPlayer(string name, Guid? id = null)
        {
            var player = UserFactory.Create(name, id ?? Guid.NewGuid());
            var result = _state.RegisterPlayer(player);
            Assert.IsTrue((bool)result.IsSuccess, "Expected player registration to succeed.");
            return player;
        }

        private void EndGame() => _state.SetPhase(GamePhase.GameOver);

        [TestMethod]
        public void Build_AlwaysIncludesMatchLevelKeys()
        {
            AddPlayer("Alice");
            AddPlayer("Bob");
            EndGame();

            var metadata = TaskMasterPlayLogMetadata.Build(_state, currentUserId: null);

            Assert.AreEqual("2", metadata["Players"]);
            Assert.IsTrue(metadata.ContainsKey("Duration"));
            // No current user → no personal keys.
            Assert.IsFalse(metadata.ContainsKey("Result"));
        }

        [TestMethod]
        public void Build_CurrentUserIsPlayer_IncludesPersonalKey()
        {
            var meId = Guid.NewGuid();
            AddPlayer("Alice");
            AddPlayer("Me", meId);
            EndGame();

            var metadata = TaskMasterPlayLogMetadata.Build(_state, meId);

            Assert.AreEqual("2", metadata["Players"]);
            Assert.AreEqual("Completed", metadata["Result"]);
        }

        [TestMethod]
        public void Build_CurrentUserNotAPlayer_OmitsPersonalKey()
        {
            AddPlayer("Alice");
            AddPlayer("Bob");
            EndGame();

            var metadata = TaskMasterPlayLogMetadata.Build(_state, Guid.NewGuid());

            Assert.IsFalse(metadata.ContainsKey("Result"));
            Assert.AreEqual("2", metadata["Players"]);
        }

        [TestMethod]
        public void Build_HostIsNotCountedAsPlayer()
        {
            // The host is never in Players, so passing the host id yields no personal key.
            EndGame();

            var metadata = TaskMasterPlayLogMetadata.Build(_state, _host.Id);

            Assert.AreEqual("0", metadata["Players"]);
            Assert.IsFalse(metadata.ContainsKey("Result"));
        }
    }
}
