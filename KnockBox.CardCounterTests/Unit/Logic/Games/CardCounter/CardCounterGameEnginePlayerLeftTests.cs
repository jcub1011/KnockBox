using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Logic.Games.CardCounter.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Threading.Tasks;

namespace KnockBoxTests.Unit.Logic.Games.CardCounter
{
    /// <summary>
    /// Tests for <see cref="CardCounterGameEngine.HandlePlayerLeft"/> covering turn-order
    /// clean-up when a player disconnects or leaves during an active game.
    /// </summary>
    [TestClass]
    public class CardCounterGameEnginePlayerLeftTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<CardCounterGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private User _player1 = default!;
        private User _player2 = default!;
        private User _player3 = default!;
        private CardCounterGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            // Return a safe default for any random call (used during InitializeGame / deck build)
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                       .Returns(1);

            _engineLoggerMock = new Mock<ILogger<CardCounterGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();

            _host = new User("Host", "host-id");
            _player1 = new User("Player1", "p1-id");
            _player2 = new User("Player2", "p2-id");
            _player3 = new User("Player3", "p3-id");

            _engine = new CardCounterGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>Creates a started game with the given players and returns the state.</summary>
        private async Task<CardCounterGameState> CreateStartedGameAsync(params User[] players)
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (CardCounterGameState)stateResult.Value!;

            foreach (var p in players)
                state.RegisterPlayer(p);

            await _engine.StartAsync(_host, state);
            return state;
        }

        // ── HandlePlayerLeft – TurnOrder clean-up ─────────────────────────────

        [TestMethod]
        public async Task HandlePlayerLeft_NonActivePlayers_RemovedFromTurnOrder()
        {
            using var state = await CreateStartedGameAsync(_player1, _player2, _player3);

            // Make p1 the active player
            state.TurnManager.SetCurrentPlayerIndex(state.TurnManager.TurnOrder.IndexOf(_player1.Id));

            // p3 (not active) leaves
            _engine.HandlePlayerLeft(_player3, state);

            Assert.IsFalse(state.TurnManager.TurnOrder.Contains(_player3.Id));
            Assert.IsFalse(state.GamePlayers.ContainsKey(_player3.Id));
        }

        [TestMethod]
        public async Task HandlePlayerLeft_NonActivePlayers_DoesNotChangeCurrentPlayerIndex()
        {
            using var state = await CreateStartedGameAsync(_player1, _player2, _player3);

            // Make p1 the active player (index 0)
            state.TurnManager.SetCurrentPlayerIndex(0);

            // p3 (last, index 2) leaves
            _engine.HandlePlayerLeft(_player3, state);

            // Index should still point to p1 (now at index 0 in the shorter list)
            Assert.AreEqual(0, state.TurnManager.CurrentPlayerIndex);
            Assert.AreEqual(_player1.Id, state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex]);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_PlayerBeforeCurrentPlayer_DecrementsCurrentPlayerIndex()
        {
            using var state = await CreateStartedGameAsync(_player1, _player2, _player3);

            // TurnOrder: [p1, p2, p3]; current = p3 (index 2)
            state.TurnManager.SetCurrentPlayerIndex(2);

            // p1 (index 0, before current) leaves
            _engine.HandlePlayerLeft(_player1, state);

            // TurnOrder: [p2, p3]; p3 is now index 1
            Assert.AreEqual(1, state.TurnManager.CurrentPlayerIndex);
            Assert.AreEqual(_player3.Id, state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex]);
        }

        // ── HandlePlayerLeft – Active player leaves ───────────────────────────

        [TestMethod]
        public async Task HandlePlayerLeft_ActivePlayer_TransitionsToPlayerTurnState()
        {
            using var state = await CreateStartedGameAsync(_player1, _player2);

            // Force the FSM into Playing phase and make p1 active
            state.SetPhase(GamePhase.Playing);
            state.TurnManager.SetCurrentPlayerIndex(0); // p1

            _engine.HandlePlayerLeft(_player1, state);

            // p1 removed; p2 is now the only player and must be active
            Assert.IsFalse(state.TurnManager.TurnOrder.Contains(_player1.Id));
            Assert.AreEqual(GamePhase.Playing, state.Phase);
            Assert.AreEqual(_player2.Id, state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex]);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_ActivePlayerLastInList_WrapsCurrentPlayerIndex()
        {
            using var state = await CreateStartedGameAsync(_player1, _player2, _player3);

            // Make p3 (last index) the active player
            state.SetPhase(GamePhase.Playing);
            state.TurnManager.SetCurrentPlayerIndex(2); // p3

            _engine.HandlePlayerLeft(_player3, state);

            // TurnOrder: [p1, p2]; index should wrap to 0
            Assert.AreEqual(0, state.TurnManager.CurrentPlayerIndex);
            Assert.AreEqual(_player1.Id, state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex]);
        }

        // ── HandlePlayerLeft – Last player leaves → GameOver ──────────────────

        [TestMethod]
        public async Task HandlePlayerLeft_LastPlayer_TransitionsToGameOver()
        {
            using var state = await CreateStartedGameAsync(_player1);

            state.SetPhase(GamePhase.Playing);

            _engine.HandlePlayerLeft(_player1, state);

            Assert.AreEqual(GamePhase.GameOver, state.Phase);
            Assert.AreEqual(0, state.TurnManager.TurnOrder.Count);
        }

        // ── HandlePlayerLeft – Game not started ───────────────────────────────

        [TestMethod]
        public async Task HandlePlayerLeft_GameNotStarted_DoesNothing()
        {
            // Game created but not started (no context)
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            // Should not throw; TurnOrder is empty because the game hasn't initialized it
            _engine.HandlePlayerLeft(_player1, state);

            Assert.AreEqual(0, state.TurnManager.TurnOrder.Count);
        }

        // ── PlayerUnregistered event wiring ───────────────────────────────────

        [TestMethod]
        public async Task PlayerUnregistered_EventFires_RemovesFromTurnOrderViaEventWiring()
        {
            // Arrange: create a started game and keep the registration token for p2.
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;

            // Register p1 and p2, keeping p2's disposal token.
            state.RegisterPlayer(_player1);
            var p2TokenResult = state.RegisterPlayer(_player2);
            Assert.IsTrue((bool)p2TokenResult.IsSuccess);
            var p2Token = p2TokenResult.Value!;

            await _engine.StartAsync(_host, state);

            state.SetPhase(GamePhase.Playing);
            Assert.IsTrue(state.TurnManager.TurnOrder.Contains(_player2.Id));

            // Act: dispose p2's token to simulate disconnect — this triggers PlayerUnregistered
            p2Token.Dispose();

            // Give Execute() a moment to complete on its own thread if needed
            await Task.Delay(100);

            // Assert: p2 removed from TurnOrder via the event wiring
            Assert.IsFalse(state.TurnManager.TurnOrder.Contains(_player2.Id));
            Assert.IsFalse(state.GamePlayers.ContainsKey(_player2.Id));
        }

        [TestMethod]
        public async Task HandlePlayerLeft_DirectCall_RemovesFromTurnOrder()
        {
            using var state = await CreateStartedGameAsync(_player1, _player2);
            state.SetPhase(GamePhase.Playing);

            _engine.HandlePlayerLeft(_player2, state);

            Assert.IsFalse(state.TurnManager.TurnOrder.Contains(_player2.Id));
            Assert.IsFalse(state.GamePlayers.ContainsKey(_player2.Id));
        }
    }
}
