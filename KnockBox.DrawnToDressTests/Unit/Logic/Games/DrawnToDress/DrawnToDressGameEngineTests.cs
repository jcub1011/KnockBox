using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;

namespace KnockBox.DrawnToDress.Tests.Unit.Logic.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressGameEngineTests
    {
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private Mock<IRandomNumberService> _randomMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _host = new User("Host", "host1");

            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object,
                _randomMock.Object);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsGameState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (DrawnToDressGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
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
            var state = (DrawnToDressGameState)stateResult.Value!;

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)startResult.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
        }

        [TestMethod]
        public async Task StartAsync_NonHostUser_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var otherUser = new User("Other", "other1");

            var startResult = await _engine.StartAsync(otherUser, state);

            Assert.IsTrue((bool)startResult.IsFailure);
        }
    }
}
