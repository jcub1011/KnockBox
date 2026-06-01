using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games
{
    [TestClass]
    public class AlphaChainGameEngineTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private AlphaChainGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", "host1");
            _engine = new AlphaChainGameEngine(
                new StubWordListService(),
                new FixedRandomNumberService(),
                new ScoreCalculator(),
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", $"p{index}-id");

        private async Task<AlphaChainGameState> CreateStateWithPlayersAsync(int count)
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (AlphaChainGameState)result.Value!;
            for (int i = 0; i < count; i++)
                state.RegisterPlayer(MakePlayer(i));
            return state;
        }

        private async Task<AlphaChainGameState> CreateStartedGameAsync(int playerCount = 2, bool hostPlays = false)
        {
            var state = await CreateStateWithPlayersAsync(playerCount);
            if (hostPlays)
                state.UpdateSettings(s => s with { HostPlays = true });
            await _engine.StartAsync(_host, state);
            return state;
        }

        // ── Engine properties ─────────────────────────────────────────────────

        [TestMethod]
        public void MinPlayerCount_IsTwo() => Assert.AreEqual(2, _engine.MinPlayerCount);

        [TestMethod]
        public void MaxPlayerCount_IsEight() => Assert.AreEqual(8, _engine.MaxPlayerCount);

        // ── CreateStateAsync ──────────────────────────────────────────────────

        [TestMethod]
        public async Task CreateStateAsync_WithNullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task CreateStateAsync_ReturnsDefaultConfig()
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (AlphaChainGameState)result.Value!;

            var defaults = new AlphaChainSettings();
            Assert.AreEqual(defaults, state.Settings);
            Assert.IsTrue(state.IsJoinable);
        }

        // ── StartAsync ────────────────────────────────────────────────────────

        [TestMethod]
        public async Task StartAsync_ClosesLobbyAndEntersRoundPhase()
        {
            using var state = await CreateStartedGameAsync(2);

            Assert.IsFalse(state.IsJoinable);
            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.IsNotNull(state.Context);
            Assert.IsNotNull(state.Context!.Fsm);
            Assert.AreEqual(1, state.CurrentEra);
            Assert.AreEqual(1, state.CurrentRound);
        }

        [TestMethod]
        public async Task StartAsPlayer_IncludesHostInTurnOrder()
        {
            using var state = await CreateStartedGameAsync(2, hostPlays: true);

            CollectionAssert.Contains(state.TurnManager.TurnOrder, _host.Id);
            Assert.IsTrue(state.GamePlayers.ContainsKey(_host.Id));
            Assert.HasCount(3, state.TurnManager.TurnOrder);
        }

        [TestMethod]
        public async Task StartAsDisplay_ExcludesHostFromTurnOrder()
        {
            using var state = await CreateStartedGameAsync(2, hostPlays: false);

            CollectionAssert.DoesNotContain(state.TurnManager.TurnOrder, _host.Id);
            Assert.IsFalse(state.GamePlayers.ContainsKey(_host.Id));
            CollectionAssert.AreEquivalent(
                state.Players.Select(p => p.User.Id).ToList(),
                state.TurnManager.TurnOrder);
        }

        // ── Turn rotation ─────────────────────────────────────────────────────

        [TestMethod]
        public async Task AdvanceTurn_RotatesPlayerInTurnOrder()
        {
            using var state = await CreateStartedGameAsync(2);
            var first = state.TurnManager.CurrentPlayer!;

            var result = await _engine.AdvanceTurnAsync(first, state);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreNotEqual(first, state.TurnManager.CurrentPlayer);
        }

        [TestMethod]
        public async Task AdvanceTurn_NotCurrentPlayer_ReturnsError()
        {
            using var state = await CreateStartedGameAsync(2);
            var notCurrent = state.TurnManager.TurnOrder[1];

            var result = await _engine.AdvanceTurnAsync(notCurrent, state);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task AdvanceTurn_WrapsAndIncrementsRound()
        {
            using var state = await CreateStartedGameAsync(2);
            Assert.AreEqual(1, state.CurrentRound);

            // Two players → two advances complete one round (the second wraps the order).
            await _engine.AdvanceTurnAsync(state.TurnManager.CurrentPlayer!, state);
            Assert.AreEqual(1, state.CurrentRound, "Mid-round advance must not bump the round.");

            await _engine.AdvanceTurnAsync(state.TurnManager.CurrentPlayer!, state);
            Assert.AreEqual(2, state.CurrentRound, "Wrapping the turn order completes the round.");
            Assert.AreEqual(0, state.TurnManager.CurrentPlayerIndex);
        }

        [TestMethod]
        public async Task Game_TransitionsToGameOver_AfterEraCountTimesEraIntervalRounds()
        {
            // 1 era × 2 rounds = 2 scheduled rounds; 2 players → 2 advances per round.
            var state = await CreateStateWithPlayersAsync(2);
            state.UpdateSettings(s => s with { EraInterval = 2, EraCount = 1 });
            await _engine.StartAsync(_host, state);
            using var _ = state;

            int lastScheduledRound = state.Settings.EraInterval * state.Settings.EraCount; // 2
            // Advance until the game ends: (players × rounds) advances at most.
            for (int i = 0; i < state.TurnManager.TurnOrder.Count * (lastScheduledRound + 1); i++)
            {
                if (state.Phase == AlphaChainGamePhase.GameOver) break;
                await _engine.AdvanceTurnAsync(state.TurnManager.CurrentPlayer!, state);
            }

            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.IsNotNull(state.Results);
            Assert.AreEqual(2, state.Results!.Standings.Count);
        }

        // ── Player-leave ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task PlayerLeaves_DuringTheirTurn_AdvancesAutomatically()
        {
            using var state = await CreateStartedGameAsync(3);
            var leaving = state.TurnManager.CurrentPlayer!;

            _engine.HandlePlayerLeft(UserFactory.Create("dummy", leaving), state);

            Assert.AreNotEqual(leaving, state.TurnManager.CurrentPlayer);
            Assert.IsTrue(state.GamePlayers[leaving].HasLeft);
        }

        [TestMethod]
        public async Task PlayerLeaves_NotTheirTurn_DoesNotAdvance()
        {
            using var state = await CreateStartedGameAsync(3);
            var current = state.TurnManager.CurrentPlayer!;
            var other = state.TurnManager.TurnOrder[1];

            _engine.HandlePlayerLeft(UserFactory.Create("dummy", other), state);

            Assert.AreEqual(current, state.TurnManager.CurrentPlayer);
            Assert.IsTrue(state.GamePlayers[other].HasLeft);
        }

        [TestMethod]
        public async Task HandlePlayerLeft_BeforeGameStarted_DoesNotThrow()
        {
            using var state = await CreateStateWithPlayersAsync(2);

            _engine.HandlePlayerLeft(MakePlayer(0), state);
        }
    }
}
