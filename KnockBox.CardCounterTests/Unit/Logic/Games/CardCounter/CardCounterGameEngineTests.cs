using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Logic.Games.CardCounter
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
            Assert.AreEqual(GamePhase.BuyIn, state.Phase);
        }

        [TestMethod]
        public async Task StartAsync_Success_ClosesJoinability()
        {
            using var state = await CreateStartedGameAsync(_player1);

            Assert.IsFalse(state.IsJoinable, "Game should not be joinable after starting.");
        }

        // ── ActiveOperatorMode start ──────────────────────────────────────────

        private async Task<CardCounterGameState> CreateStartedActiveOperatorGameAsync(params User[] players)
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (CardCounterGameState)stateResult.Value!;
            state.Config.ActiveOperatorMode = true;
            foreach (var p in players)
                state.RegisterPlayer(p);
            await _engine.StartAsync(_host, state);
            return state;
        }

        [TestMethod]
        public async Task StartAsync_ActiveOperatorMode_SkipsBuyInPhase()
        {
            using var state = await CreateStartedActiveOperatorGameAsync(_player1);

            // Active Operator Mode skips the buy-in phase; game should be in Playing state
            Assert.AreEqual(GamePhase.Playing, state.Phase,
                "Active Operator Mode should skip BuyIn and go straight to Playing.");
        }

        [TestMethod]
        public async Task StartAsync_ActiveOperatorMode_SetsAllBalancesToTen()
        {
            using var state = await CreateStartedActiveOperatorGameAsync(_player1);

            foreach (var ps in state.GamePlayers.Values)
                Assert.AreEqual(10.0, ps.Balance,
                    $"Player [{ps.PlayerId}] should start with balance 10 in Active Operator Mode.");
        }

        [TestMethod]
        public async Task StartAsync_ActiveOperatorMode_SetsHasSetBuyInForAllPlayers()
        {
            using var state = await CreateStartedActiveOperatorGameAsync(_player1);

            foreach (var ps in state.GamePlayers.Values)
                Assert.IsTrue(ps.HasSetBuyIn,
                    $"Player [{ps.PlayerId}] should have HasSetBuyIn=true after Active Operator Mode start.");
        }

        [TestMethod]
        public async Task StartAsync_ActiveOperatorMode_MultiplePlayersAllGetBalanceTen()
        {
            var player2 = new User("Player2", "p2-id");
            using var state = await CreateStartedActiveOperatorGameAsync(_player1, player2);

            Assert.HasCount(2, state.GamePlayers);
            foreach (var ps in state.GamePlayers.Values)
                Assert.AreEqual(10.0, ps.Balance,
                    $"All players should start with balance 10 in Active Operator Mode.");
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

        [TestMethod]
        public async Task AcceptPending_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.AcceptPending(_player1, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task SubmitReorder_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.SubmitReorder(_player1, state, [0, 1, 2]);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task DiscardActionCards_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.DiscardActionCards(_player1, state, [0]);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task SkimSelect_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.SkimSelect(_player1, state, 0, 0);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task NotMyMoneySelectTarget_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.NotMyMoneySelectTarget(_player1, state, "target-id");

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task NotMyMoneyCancel_BeforeGameStarted_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            using var state = (CardCounterGameState)stateResult.Value!;
            state.RegisterPlayer(_player1);

            var result = _engine.NotMyMoneyCancel(_player1, state);

            Assert.IsTrue(result.IsFailure);
        }

        // ── EnableActionTimer ─────────────────────────────────────────────────

        [TestMethod]
        public async Task Tick_WhenActionTimerDisabled_DoesNotAdvanceFsmState()
        {
            using var state = await CreateStartedGameAsync(_player1);

            var context = state.Context!;
            state.Config.EnableActionTimer = false;
            var p1 = state.GamePlayers.Values.First();
            state.CurrentShoe.Push(new NumberCard(3));
            state.CurrentShoe.Push(new NumberCard(7));

            var playerTurnState = new PlayerTurnState();
            context.Fsm.TransitionTo(context, playerTurnState);
            var potBefore = p1.Pot.Count;

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.HasCount(potBefore, p1.Pot, "No card should be auto-drawn when the action timer is disabled.");
            Assert.AreSame(playerTurnState, context.Fsm.CurrentState, "FSM state should not change when action timer is disabled.");
        }

        // ── ResetGame ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task ResetGame_ByNonHost_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            var nonHost = new User("NotHost", "nothost-id");
            var result = _engine.ResetGame(nonHost, state);

            Assert.IsTrue(result.IsFailure, "Non-host should not be able to reset the game.");
        }

        [TestMethod]
        public async Task ResetGame_DuringActiveGame_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.Playing);

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue(result.IsFailure, "Reset should only be allowed after the game is over.");
        }

        [TestMethod]
        public async Task ResetGame_DuringBuyIn_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            // Phase is already BuyIn after start

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_TransitionsToBuyIn()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.BuyIn, state.Phase, "Reset should transition back to BuyIn.");
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_ClearsDiscardHistory()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.DiscardHistory.Add(new DiscardHistoryEntry("# 5", "🔢", "Player", false));
            state.SetPhase(GamePhase.GameOver);

            _engine.ResetGame(_host, state);

            Assert.IsEmpty(state.DiscardHistory, "Discard history should be cleared on reset.");
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_ClearsLastPlayedAction()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.LastPlayedAction = new LastPlayedActionInfo("p1", "P1", ActionType.Burn, null, null);
            state.SetPhase(GamePhase.GameOver);

            _engine.ResetGame(_host, state);

            Assert.IsNull(state.LastPlayedAction, "LastPlayedAction should be cleared on reset.");
        }

        [TestMethod]
        public async Task ResetGame_AfterGameOver_RebuildsDeck()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            // Main deck should be empty (exhausted to trigger game over)
            state.MainDeck.Clear();
            state.CurrentShoe.Clear();

            _engine.ResetGame(_host, state);

            // After reset, deck should be rebuilt and BuyIn state sets up shoe via RoundEnd
            Assert.AreEqual(GamePhase.BuyIn, state.Phase);
        }

        [TestMethod]
        public async Task ResetGame_ActiveOperatorMode_SkipsBuyInPhase()
        {
            using var state = await CreateStartedActiveOperatorGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.Playing, state.Phase,
                "Active Operator Mode reset should skip BuyIn and go straight to Playing.");
        }

        [TestMethod]
        public async Task ResetGame_ActiveOperatorMode_SetsAllBalancesToTen()
        {
            using var state = await CreateStartedActiveOperatorGameAsync(_player1);
            // Simulate players having earned/lost balance during the game
            foreach (var ps in state.GamePlayers.Values)
                ps.Balance = 999;
            state.SetPhase(GamePhase.GameOver);

            _engine.ResetGame(_host, state);

            foreach (var ps in state.GamePlayers.Values)
                Assert.AreEqual(10.0, ps.Balance,
                    $"Player [{ps.PlayerId}] should be reset to balance 10 in Active Operator Mode.");
        }

        // ── ReturnToLobby ─────────────────────────────────────────────────────

        [TestMethod]
        public async Task ReturnToLobby_ByNonHost_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            var nonHost = new User("NotHost", "nothost-id");
            var result = _engine.ReturnToLobby(nonHost, state);

            Assert.IsTrue(result.IsFailure, "Non-host should not be able to return to the lobby.");
        }

        [TestMethod]
        public async Task ReturnToLobby_DuringActiveGame_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.Playing);

            var result = _engine.ReturnToLobby(_host, state);

            Assert.IsTrue(result.IsFailure, "Return to lobby should only be allowed after the game is over.");
        }

        [TestMethod]
        public async Task ReturnToLobby_DuringBuyIn_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(_player1);
            // Phase is already BuyIn after start

            var result = _engine.ReturnToLobby(_host, state);

            Assert.IsTrue(result.IsFailure, "Return to lobby should only be allowed after the game is over.");
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterGameOver_MakesStateJoinable()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            var result = _engine.ReturnToLobby(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsTrue(state.IsJoinable, "State should be joinable after returning to lobby.");
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterGameOver_ClearsContext()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            _engine.ReturnToLobby(_host, state);

            Assert.IsNull(state.Context, "Context should be cleared when returning to the lobby.");
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterGameOver_ClearsGamePlayers()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);

            _engine.ReturnToLobby(_host, state);

            Assert.IsEmpty(state.GamePlayers, "GamePlayers should be cleared when returning to the lobby.");
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterGameOver_ClearsDiscardHistory()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.DiscardHistory.Add(new DiscardHistoryEntry("# 5", "🔢", "Player", false));
            state.SetPhase(GamePhase.GameOver);

            _engine.ReturnToLobby(_host, state);

            Assert.IsEmpty(state.DiscardHistory, "Discard history should be cleared when returning to the lobby.");
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterGameOver_ClearsLastPlayedAction()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.LastPlayedAction = new LastPlayedActionInfo("p1", "P1", ActionType.Burn, null, null);
            state.SetPhase(GamePhase.GameOver);

            _engine.ReturnToLobby(_host, state);

            Assert.IsNull(state.LastPlayedAction, "LastPlayedAction should be cleared when returning to the lobby.");
        }

        [TestMethod]
        public async Task ReturnToLobby_AfterReturnToLobby_CanStartAgain()
        {
            using var state = await CreateStartedGameAsync(_player1);
            state.SetPhase(GamePhase.GameOver);
            _engine.ReturnToLobby(_host, state);

            // After returning to lobby, the game should be startable again
            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess, "Should be able to start the game again after returning to the lobby.");
            Assert.IsFalse(state.IsJoinable, "State should not be joinable once the game has started.");
        }
    }
}
