using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.LinkedList.Tests.Unit.Logic
{
    [TestClass]
    public class LinkedListGameEngineTests
    {
        private Mock<ILogger<LinkedListGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<LinkedListGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private LinkedListGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<LinkedListGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<LinkedListGameState>>();
            _host = UserFactory.Create("Host", "host1");

            _engine = new LinkedListGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsJoinableState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue(result.IsSuccess);
            var state = (LinkedListGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsFailure()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_SetsNotJoinable()
        {
            var createResult = await _engine.CreateStateAsync(_host);
            var state = (LinkedListGameState)createResult.Value!;
            Assert.IsTrue(state.IsJoinable);

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue(startResult.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }
    }
}
