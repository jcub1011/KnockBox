using KnockBox.Core.Extensions.Returns;
using KnockBox.TaskMaster.Services.Logic.Games;
using KnockBox.TaskMaster.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.TaskMaster.Tests.Unit.Logic
{
    [TestClass]
    public class TaskMasterGameEngineTests
    {
        private Mock<ILogger<TaskMasterGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<TaskMasterGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private TaskMasterGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<TaskMasterGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<TaskMasterGameState>>();
            _host = new User("Host", "host1");

            _engine = new TaskMasterGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsGameState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (TaskMasterGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_ValidHostAndState_StartsGame()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (TaskMasterGameState)stateResult.Value!;

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
            Assert.AreEqual(GamePhase.Playing, state.Phase);
        }

        [TestMethod]
        public async Task StartAsync_InvalidHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (TaskMasterGameState)stateResult.Value!;
            var nonHost = new User("Not Host", "non-host");

            var result = await _engine.StartAsync(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_InvalidStateType_ReturnsError()
        {
            var result = await _engine.StartAsync(_host, null!);
            Assert.IsTrue((bool)result.IsFailure);
        }
    }
}
