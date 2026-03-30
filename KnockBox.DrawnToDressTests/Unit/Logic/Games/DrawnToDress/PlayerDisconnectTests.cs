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
    public class PlayerDisconnectTests
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
        public async Task PlayerDisconnect_SetsIsDisconnectedTrue()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);

            var player = new User("Player1", "p1");
            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };

            // Act: simulate disconnect by calling HandlePlayerLeft directly.
            _engine.HandlePlayerLeft(player, state);

            // Assert
            Assert.IsTrue(state.GamePlayers["p1"].IsDisconnected);
        }

        [TestMethod]
        public async Task PlayerDisconnect_SetsIsReadyTrue()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);

            var player = new User("Player1", "p1");
            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1", IsReady = false };

            // Act
            _engine.HandlePlayerLeft(player, state);

            // Assert
            Assert.IsTrue(state.GamePlayers["p1"].IsReady);
        }

        [TestMethod]
        public async Task DisconnectedPlayer_DoesNotBlockAllPlayersReady()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var disconnectedUser = new User("Player1", "p1");
            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1", IsReady = false };
            state.GamePlayers["p2"] = new DrawnToDressPlayerState { PlayerId = "p2", IsReady = true };

            // Act: disconnect p1, which should auto-ready them.
            _engine.HandlePlayerLeft(disconnectedUser, state);

            // Assert: both players are now ready.
            Assert.IsTrue(context.AllPlayersReady());
        }
    }
}
