using KnockBox.TaskMaster.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.TaskMaster.Tests.Unit.State
{
    [TestClass]
    public class TaskMasterGameStateTests
    {
        private Mock<ILogger<TaskMasterGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<TaskMasterGameState>>();
            _host = new User("HostUser", "host-id");
        }

        [TestMethod]
        public void Constructor_SetsHostAndDefaultPhase()
        {
            using var state = new TaskMasterGameState(_host, _loggerMock.Object);

            Assert.AreSame(_host, state.Host);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public void SetPhase_UpdatesPhase()
        {
            using var state = new TaskMasterGameState(_host, _loggerMock.Object);

            state.SetPhase(GamePhase.Playing);
            Assert.AreEqual(GamePhase.Playing, state.Phase);

            state.SetPhase(GamePhase.GameOver);
            Assert.AreEqual(GamePhase.GameOver, state.Phase);
        }
    }
}
