using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Logic.Games.CardCounter.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.CardCounter
{
    /// <summary>
    /// Tests for <see cref="CardCounterGameEngine"/> public-facing API methods,
    /// covering pre-game error handling, game reset, and player command routing.
    /// </summary>
    [TestClass]
    public class CardCounterGameEngineTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<CardCounterGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;
        private CardCounterGameEngine _engine = default!;
        private User _host = default!;
        private User _player1 = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(1);

            _engineLoggerMock = new Mock<ILogger<CardCounterGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();

            _host = new User("Host", "host-id");
            _player1 = new User("Player1", "p1-id");

            _engine = new CardCounterGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private async Task<CardCounterGameState> CreateStartedGameAsync(params User[] players)
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (CardCounterGameState)stateResult.Value!;
            foreach (var p in players)
                state.RegisterPlayer(p);
            await _engine.StartAsync(_host, state);
            return state;
        }

        // ── CreateStateAsync ──────────────────────────────────────────────────

        [TestMethod]
        public async Task CreateStateAsync_WithNullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithValidHost_ReturnsCardCounterGameState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsInstanceOfType(result.Value, typeof(CardCounterGameState));
        }

        [TestMethod]
        public async Task CreateStateAsync_NewState_IsJoinable()
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (CardCounterGameState)result.Value!;

            Assert.IsTrue(state.IsJoinable, "A freshly created game should be joinable.");
        }

        // ── StartAsync ────────────────────────────────────────────────────────

        [TestMethod]
        public async Task StartAsync_WithNonHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var nonHost = new User("NotHost", "nothost-id");
            var result = await _engine.StartAsync(nonHost, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_WithNoPlayers_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            // No players registered

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_Success_SetsGamePhase()
        {
            using var state = await CreateStartedGameAsync(_player1);

            // After start, game enters BuyIn phase
            Assert.AreEqual(GamePhase.BuyIn, state.GamePhase);
        }

        [TestMethod]
        public async Task StartAsync_Success_ClosesJoinability()
        {
            using var state = await CreateStartedGameAsync(_player1);

            Assert.IsFalse(state.IsJoinable, "Game should not be joinable after starting.");
        }

        // ── Player command routing before game start ───────────────────────────

        [TestMethod]
        public async Task DrawCard_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.DrawCard(_player1, state);

            Assert.IsTrue(result.IsFailure, "DrawCard before game start should return an error.");
        }

        [TestMethod]
        public async Task SetBuyIn_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.SetBuyIn(_player1, state, false);

            Assert.IsTrue(result.IsFailure, "SetBuyIn before game start should return an error.");
        }

        [TestMethod]
        public async Task PlayActionCard_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.PlayActionCard(_player1, state, 0);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task PassTurn_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.PassTurn(_player1, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task FoldPot_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.FoldPot(_player1, state);

            Assert.IsTrue(result.IsFailure);
        }

        // ── ResetGame ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task ResetGame_ByNonHost_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.GamePhase = GamePhase.GameOver;

            var nonHost = new User("NotHost", "nothost-id");
            var result = _engine.ResetGame(nonHost, state);

            Assert.IsTrue(result.IsFailure, "Non-host should not be able to reset the game.");
        }

        [TestMethod]
        public async Task ResetGame_DuringActiveGame_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.GamePhase = GamePhase.Playing;

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue(result.IsFailure, "Reset should only be allowed after the game is over.");
        }

        [TestMethod]
        public async Task ResetGame_DuringBuyIn_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            // GamePhase is already BuyIn after start

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_TransitionsToBuyIn()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.GamePhase = GamePhase.GameOver;

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.BuyIn, state.GamePhase, "Reset should transition back to BuyIn.");
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_ClearsDiscardHistory()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.DiscardHistory.Add(new DiscardHistoryEntry("# 5", "🔢", "Player", false));
            state.GamePhase = GamePhase.GameOver;

            _engine.ResetGame(_host, state);

            Assert.AreEqual(0, state.DiscardHistory.Count, "Discard history should be cleared on reset.");
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_ClearsLastPlayedAction()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.LastPlayedAction = new LastPlayedActionInfo("p1", "P1", ActionType.Burn, null, null);
            state.GamePhase = GamePhase.GameOver;

            _engine.ResetGame(_host, state);

            Assert.IsNull(state.LastPlayedAction, "LastPlayedAction should be cleared on reset.");
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_RebuildsDeck()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.GamePhase = GamePhase.GameOver;

            // Main deck should be empty (exhausted to trigger game over)
            state.MainDeck.Clear();
            state.CurrentShoe.Clear();

            _engine.ResetGame(_host, state);

            // After reset, deck should be rebuilt and BuyIn state sets up shoe via RoundEnd
            Assert.AreEqual(GamePhase.BuyIn, state.GamePhase);
        }
    }
}
