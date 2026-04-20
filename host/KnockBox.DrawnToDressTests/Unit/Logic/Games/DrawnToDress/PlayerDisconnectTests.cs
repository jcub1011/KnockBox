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

        [TestMethod]
        public async Task HandlePlayerLeft_UnknownPlayer_DoesNotThrow()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);

            var unknownPlayer = new User("Ghost", "unknown-id");

            // Act: should not throw — the guard clause returns early.
            _engine.HandlePlayerLeft(unknownPlayer, state);

            // Assert: no player was added.
            Assert.IsFalse(state.GamePlayers.ContainsKey("unknown-id"));
        }

        [TestMethod]
        public async Task HandlePlayerLeft_CalledTwice_DoesNotThrow()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);

            var player = new User("Player1", "p1");
            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };

            // Act: disconnect twice.
            _engine.HandlePlayerLeft(player, state);
            _engine.HandlePlayerLeft(player, state);

            // Assert: still disconnected and ready, no exception.
            Assert.IsTrue(state.GamePlayers["p1"].IsDisconnected);
            Assert.IsTrue(state.GamePlayers["p1"].IsReady);
        }

        [TestMethod]
        public async Task DisconnectedPlayer_RemainingPlayerReadies_AllPlayersReadyTransitions()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1", IsReady = false };
            state.GamePlayers["p2"] = new DrawnToDressPlayerState { PlayerId = "p2", IsReady = false };

            // Act: disconnect p1, then p2 readies up.
            _engine.HandlePlayerLeft(new User("Player1", "p1"), state);

            Assert.IsTrue(state.GamePlayers["p1"].IsReady);
            Assert.IsFalse(state.GamePlayers["p2"].IsReady);
            Assert.IsFalse(context.AllPlayersReady());

            state.GamePlayers["p2"].IsReady = true;

            // Assert: now all players are ready.
            Assert.IsTrue(context.AllPlayersReady());
        }
    }
}
