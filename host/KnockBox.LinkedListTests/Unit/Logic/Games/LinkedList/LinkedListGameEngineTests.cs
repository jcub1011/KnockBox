using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using KnockBox.LinkedList.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.LinkedList.Tests.Unit.Logic
{
    [TestClass]
    public class LinkedListGameEngineTests
    {
        private Mock<ILogger<LinkedListGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<LinkedListGameState>> _stateLoggerMock = default!;
        private WordPairSource _wordPairSource = default!;
        private SequentialRng _rng = default!;
        private User _host = default!;
        private LinkedListGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<LinkedListGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<LinkedListGameState>>();
            _wordPairSource = new WordPairSource();
            _rng = new SequentialRng(0);
            _host = UserFactory.Create("Host", "host1");

            _engine = new LinkedListGameEngine(
                _wordPairSource,
                _rng,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private async Task<LinkedListGameState> CreateWithPlayersAsync(int playerCount)
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (LinkedListGameState)result.Value!;
            for (int i = 0; i < playerCount; i++)
            {
                state.RegisterPlayer(UserFactory.Create($"P{i}", $"p{i}"));
            }
            return state;
        }

        [TestMethod]
        public async Task CreateStateAsync_ReturnsJoinableSetupState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue(result.IsSuccess);
            var state = (LinkedListGameState)result.Value!;
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
        }

        [TestMethod]
        public async Task PlayerBounds_AreThreeToTen()
        {
            await Task.CompletedTask;
            Assert.AreEqual(3, _engine.MinPlayerCount);
            Assert.AreEqual(10, _engine.MaxPlayerCount);
        }

        [TestMethod]
        public async Task StartAsync_WithThreePlayers_AdvancesToPlaying()
        {
            var state = await CreateWithPlayersAsync(3);

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue(startResult.IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
            Assert.IsFalse(state.IsJoinable);
            Assert.AreEqual(3, state.TurnManager.TurnOrder.Count);
            Assert.AreEqual(3, state.GamePlayers.Count);
            Assert.IsFalse(string.IsNullOrEmpty(state.StartWord));
            Assert.IsFalse(string.IsNullOrEmpty(state.DestinationWord));
            Assert.AreEqual(state.StartWord, state.CarriedWord);
            Assert.IsFalse(string.IsNullOrEmpty(state.AuditorPlayerId));
        }

        [TestMethod]
        public async Task StartAsync_AssignsAuditorThatIsNotTheCurrentSubmitter()
        {
            var state = await CreateWithPlayersAsync(3);

            await _engine.StartAsync(_host, state);

            Assert.AreNotEqual(state.TurnManager.CurrentPlayer, state.AuditorPlayerId);
            CollectionAssert.Contains(state.TurnManager.TurnOrder, state.AuditorPlayerId);
        }

        [TestMethod]
        public async Task StartAsync_HonorsHostChosenWords()
        {
            var state = await CreateWithPlayersAsync(3);
            state.Execute(() =>
            {
                state.StartWord = "ALPHA";
                state.DestinationWord = "OMEGA";
            });

            await _engine.StartAsync(_host, state);

            Assert.AreEqual("ALPHA", state.StartWord);
            Assert.AreEqual("OMEGA", state.DestinationWord);
            Assert.AreEqual("ALPHA", state.CarriedWord);
        }

        [TestMethod]
        public async Task CanStartAsync_FailsWithTooFewPlayers()
        {
            var state = await CreateWithPlayersAsync(2);

            Assert.IsFalse(await _engine.CanStartAsync(state));
        }

        [TestMethod]
        public async Task StartAsync_WithTooFewPlayers_StaysInSetup()
        {
            // StartAsyncCore itself runs (host-authorized), but with < min players the
            // lobby is not startable; verify CanStartAsync gates it.
            var state = await CreateWithPlayersAsync(2);

            Assert.IsFalse(await _engine.CanStartAsync(state));
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
            Assert.IsTrue(state.IsJoinable);
        }
    }
}
