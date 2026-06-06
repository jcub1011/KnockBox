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
            _host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));

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

            var player = UserFactory.Create("Player1", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111") };

            // Act: simulate disconnect by calling HandlePlayerLeft directly.
            _engine.HandlePlayerLeft(player, state);

            // Assert
            Assert.IsTrue(state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].IsDisconnected);
        }

        [TestMethod]
        public async Task PlayerDisconnect_SetsIsReadyTrue()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);

            var player = UserFactory.Create("Player1", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"), IsReady = false };

            // Act
            _engine.HandlePlayerLeft(player, state);

            // Assert
            Assert.IsTrue(state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].IsReady);
        }

        [TestMethod]
        public async Task DisconnectedPlayer_DoesNotBlockAllPlayersReady()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var disconnectedUser = UserFactory.Create("Player1", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"), IsReady = false };
            state.GamePlayers[Guid.Parse("22222222-2222-2222-2222-222222222222")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"), IsReady = true };

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

            var unknownPlayer = UserFactory.Create("Ghost", Guid.Parse("99999999-9999-9999-9999-999999999999"));

            // Act: should not throw — the guard clause returns early.
            _engine.HandlePlayerLeft(unknownPlayer, state);

            // Assert: no player was added.
            Assert.IsFalse(state.GamePlayers.ContainsKey(Guid.Parse("99999999-9999-9999-9999-999999999999")));
        }

        [TestMethod]
        public async Task HandlePlayerLeft_CalledTwice_DoesNotThrow()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);

            var player = UserFactory.Create("Player1", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111") };

            // Act: disconnect twice.
            _engine.HandlePlayerLeft(player, state);
            _engine.HandlePlayerLeft(player, state);

            // Assert: still disconnected and ready, no exception.
            Assert.IsTrue(state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].IsDisconnected);
            Assert.IsTrue(state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].IsReady);
        }

        [TestMethod]
        public async Task DisconnectedPlayer_RemainingPlayerReadies_AllPlayersReadyTransitions()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"), IsReady = false };
            state.GamePlayers[Guid.Parse("22222222-2222-2222-2222-222222222222")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"), IsReady = false };

            // Act: disconnect p1, then p2 readies up.
            _engine.HandlePlayerLeft(UserFactory.Create("Player1", Guid.Parse("11111111-1111-1111-1111-111111111111")), state);

            Assert.IsTrue(state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].IsReady);
            Assert.IsFalse(state.GamePlayers[Guid.Parse("22222222-2222-2222-2222-222222222222")].IsReady);
            Assert.IsFalse(context.AllPlayersReady());

            state.GamePlayers[Guid.Parse("22222222-2222-2222-2222-222222222222")].IsReady = true;

            // Assert: now all players are ready.
            Assert.IsTrue(context.AllPlayersReady());
        }
    }
}
