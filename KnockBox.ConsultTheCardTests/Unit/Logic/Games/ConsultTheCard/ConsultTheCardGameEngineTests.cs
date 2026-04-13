using KnockBox.Services.Logic.Games.ConsultTheCard;
using KnockBox.Services.Logic.Games.ConsultTheCard.FSM;
using KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCardTests.Unit.Logic.Games.ConsultTheCard
{
    /// <summary>
    /// Tests for <see cref="ConsultTheCardGameEngine"/> public-facing API methods,
    /// covering lifecycle, UI command routing, and player-leave handling.
    /// </summary>
    [TestClass]
    public class ConsultTheCardGameEngineTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<ConsultTheCardGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<ConsultTheCardGameState>> _stateLoggerMock = default!;
        private ConsultTheCardGameEngine _engine = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            int callCount = 0;
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) => { callCount++; return callCount % 2 == 0 ? 1 % max : 0; });
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int min, int max, RandomType _) => min);

            _engineLoggerMock = new Mock<ILogger<ConsultTheCardGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<ConsultTheCardGameState>>();

            _host = new User("Host", "host-id");

            _engine = new ConsultTheCardGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private User MakePlayer(int index) => new($"Player{index}", $"p{index}-id");

        private async Task<ConsultTheCardGameState> CreateStateWithPlayersAsync(int count)
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (ConsultTheCardGameState)result.Value!;
            for (int i = 0; i < count; i++)
                state.RegisterPlayer(MakePlayer(i));
            return state;
        }

        private async Task<ConsultTheCardGameState> CreateStartedGameAsync(int playerCount = 4)
        {
            var state = await CreateStateWithPlayersAsync(playerCount);
            await _engine.StartAsync(_host, state);
            return state;
        }

        // ── Engine properties ─────────────────────────────────────────────────

        [TestMethod]
        public void MinPlayerCount_IsFour()
        {
            Assert.AreEqual(4, _engine.MinPlayerCount);
        }

        [TestMethod]
        public void MaxPlayerCount_IsEight()
        {
            Assert.AreEqual(8, _engine.MaxPlayerCount);
        }

        // ── CreateStateAsync ──────────────────────────────────────────────────

        [TestMethod]
        public async Task CreateStateAsync_WithNullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithValidHost_ReturnsState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsInstanceOfType(result.Value, typeof(ConsultTheCardGameState));
        }

        [TestMethod]
        public async Task CreateStateAsync_NewState_IsJoinable()
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (ConsultTheCardGameState)result.Value!;

            Assert.IsTrue(state.IsJoinable);
        }

        // ── StartAsync ────────────────────────────────────────────────────────

        [TestMethod]
        public async Task StartAsync_WithWrongStateType_ReturnsError()
        {
            var mockState = new Mock<KnockBox.Services.State.Games.Shared.AbstractGameState>(
                _host, Mock.Of<ILogger>());

            var result = await _engine.StartAsync(_host, mockState.Object);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_WithNonHost_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);
            var nonHost = new User("NotHost", "nothost-id");

            var result = await _engine.StartAsync(nonHost, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_Success_SetsGamePhaseToSetup()
        {
            using var state = await CreateStartedGameAsync(4);

            Assert.AreEqual(ConsultTheCardGamePhase.Setup, state.Phase);
        }

        [TestMethod]
        public async Task StartAsync_Success_ClosesJoinability()
        {
            using var state = await CreateStartedGameAsync(4);

            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_Success_AssignsContextAndFsm()
        {
            using var state = await CreateStartedGameAsync(4);

            Assert.IsNotNull(state.Context);
            Assert.IsNotNull(state.Context!.Fsm);
        }

        [TestMethod]
        public async Task StartAsync_Success_PopulatesGamePlayersAndTurnOrder()
        {
            using var state = await CreateStartedGameAsync(4);

            Assert.AreEqual(4, state.GamePlayers.Count);
            Assert.AreEqual(4, state.TurnManager.TurnOrder.Count);
        }

        [TestMethod]
        public async Task StartAsync_WithTooFewPlayers_CanStartReturnsFalse()
        {
            // Create state with only 3 players (min is 4).
            var state = await CreateStateWithPlayersAsync(3);

            var canStart = await _engine.CanStartAsync(state);

            Assert.IsFalse(canStart, "CanStartAsync should return false with fewer than 4 players.");
        }

        [TestMethod]
        public async Task StartAsync_WithTooManyPlayers_CanStartReturnsFalse()
        {
            // Create state with 9 players (max is 8).
            var state = await CreateStateWithPlayersAsync(9);

            var canStart = await _engine.CanStartAsync(state);

            Assert.IsFalse(canStart, "CanStartAsync should return false with more than 8 players.");
        }

        // ── UI methods before game start return errors ────────────────────────

        [TestMethod]
        public async Task SubmitClue_BeforeGameStarted_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = _engine.SubmitClue(MakePlayer(0), state, "test");

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task CastVote_BeforeGameStarted_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = _engine.CastVote(MakePlayer(0), state, "p1-id");

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task InformantGuess_BeforeGameStarted_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = _engine.InformantGuess(MakePlayer(0), state, "word");

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task AdvanceToVote_BeforeGameStarted_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = _engine.AdvanceToVote(_host, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task VoteToEndGame_BeforeGameStarted_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = _engine.VoteToEndGame(MakePlayer(0), state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task StartNextGame_BeforeGameStarted_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = _engine.StartNextGame(_host, state);

            Assert.IsTrue(result.IsFailure);
        }

        // ── ReturnToLobby ─────────────────────────────────────────────────────

        [TestMethod]
        public async Task ReturnToLobby_NonHost_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(4);
            var nonHost = new User("NotHost", "nothost-id");

            var result = _engine.ReturnToLobby(nonHost, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task ReturnToLobby_Host_ClearsContextAndSetsJoinable()
        {
            using var state = await CreateStartedGameAsync(4);

            var result = _engine.ReturnToLobby(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsNull(state.Context);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task ReturnToLobby_Host_ClearsGameState()
        {
            using var state = await CreateStartedGameAsync(4);

            _engine.ReturnToLobby(_host, state);

            Assert.AreEqual(0, state.GamePlayers.Count);
            Assert.AreEqual(0, state.TurnManager.TurnOrder.Count);
            Assert.AreEqual(0, state.TurnManager.CurrentPlayerIndex);
        }

        // ── ResetGame ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task ResetGame_NonHost_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(4);
            var nonHost = new User("NotHost", "nothost-id");

            var result = _engine.ResetGame(nonHost, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task ResetGame_Host_ResetsAndTransitionsToSetup()
        {
            using var state = await CreateStartedGameAsync(4);

            var result = _engine.ResetGame(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsNotNull(state.Context);
            Assert.AreEqual(ConsultTheCardGamePhase.Setup, state.Phase);
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Tick_DelegatesToFsm()
        {
            using var state = await CreateStartedGameAsync(4);
            var context = state.Context!;

            // Tick before timeout should not error
            var result = _engine.Tick(context, DateTimeOffset.UtcNow);

            Assert.IsTrue((bool)result.IsSuccess);
        }

        [TestMethod]
        public async Task Tick_AlwaysDelegatesRegardlessOfEnableTimers()
        {
            using var state = await CreateStartedGameAsync(4);
            state.Config.EnableTimers = false;
            var context = state.Context!;

            // Tick should still succeed even with timers disabled
            var result = _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsTrue((bool)result.IsSuccess);
        }

        // ── HandlePlayerLeft ──────────────────────────────────────────────────

        [TestMethod]
        public async Task HandlePlayerLeft_RemovesFromTurnOrder()
        {
            using var state = await CreateStartedGameAsync(4);
            var leavingPlayer = MakePlayer(0);
            int initialCount = state.TurnManager.TurnOrder.Count;

            _engine.HandlePlayerLeft(leavingPlayer, state);

            Assert.AreEqual(initialCount - 1, state.TurnManager.TurnOrder.Count);
            Assert.IsFalse(state.TurnManager.TurnOrder.Contains(leavingPlayer.Id));
        }

        [TestMethod]
        public async Task HandlePlayerLeft_MarksPlayerAsEliminated()
        {
            using var state = await CreateStartedGameAsync(4);
            var leavingPlayer = MakePlayer(0);

            _engine.HandlePlayerLeft(leavingPlayer, state);

            var ps = state.Context!.GetPlayer(leavingPlayer.Id);
            Assert.IsNotNull(ps);
            Assert.IsTrue(ps.IsEliminated);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_BeforeGameStarted_DoesNotThrow()
        {
            using var state = await CreateStateWithPlayersAsync(4);
            var player = MakePlayer(0);

            // Should not throw when context is null
            _engine.HandlePlayerLeft(player, state);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_AdjustsCluePlayerIndex_WhenBeforeCurrent()
        {
            using var state = await CreateStartedGameAsync(5);
            // Set the current clue player to index 3
            state.TurnManager.SetCurrentPlayerIndex(3);
            // Remove player at index 1 (before current)
            string leavingPlayerId = state.TurnManager.TurnOrder[1];

            _engine.HandlePlayerLeft(new User(leavingPlayerId, leavingPlayerId), state);

            Assert.AreEqual(2, state.TurnManager.CurrentPlayerIndex);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_DuringVotePhase_VoidsVotesForDisconnectedPlayer()
        {
            using var state = await CreateStartedGameAsync(4);
            var context = state.Context!;

            // Advance to vote phase
            state.SetPhase(ConsultTheCardGamePhase.Voting);

            // Have a player vote for the player who will leave
            var leavingPlayerId = state.TurnManager.TurnOrder[0];
            var votingPlayerId = state.TurnManager.TurnOrder[1];
            var voterState = context.GetPlayer(votingPlayerId)!;
            voterState.HasVoted = true;
            voterState.VoteTargetId = leavingPlayerId;

            _engine.HandlePlayerLeft(new User("dummy", leavingPlayerId), state);

            // Voter's vote should be voided
            Assert.IsFalse(voterState.HasVoted);
            Assert.IsNull(voterState.VoteTargetId);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_WhenTooFewPlayersRemain_TransitionsToGameOver()
        {
            using var state = await CreateStartedGameAsync(4);

            // Remove players until <= 2 remain (4 players, remove 2)
            var player0 = new User("dummy", state.TurnManager.TurnOrder[0]);
            var player1 = new User("dummy", state.TurnManager.TurnOrder[1]);

            _engine.HandlePlayerLeft(player0, state);
            _engine.HandlePlayerLeft(player1, state);

            Assert.AreEqual(ConsultTheCardGamePhase.GameOver, state.Phase);
        }
    }
}
