using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain
{
    /// <summary>
    /// Verifies the player-leave handler: a departure during your turn auto-advances so the
    /// game does not stall, and the player is only marked eliminated in Survival mode.
    /// </summary>
    [TestClass]
    public class AlphaChainGameEnginePlayerLeftTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", "host1");
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", $"p{index}-id");

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            int playerCount, bool survival)
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService(), new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            if (survival)
                state.UpdateSettings(s => s with { SurvivalMode = true });

            await engine.StartAsync(_host, state);
            return (engine, state);
        }

        [TestMethod]
        public async Task PlayerLeaves_DuringTurn_NonSurvival_AdvancesAndDoesNotEliminate()
        {
            var (engine, state) = await StartGameAsync(playerCount: 3, survival: false);
            using var _ = state;
            var leaving = state.TurnManager.CurrentPlayer!;

            engine.HandlePlayerLeft(UserFactory.Create("dummy", leaving), state);

            Assert.IsTrue(state.GamePlayers[leaving].HasLeft);
            Assert.IsFalse(state.GamePlayers[leaving].IsEliminated);
            Assert.AreNotEqual(leaving, state.TurnManager.CurrentPlayer);
            Assert.AreNotEqual(AlphaChainGamePhase.GameOver, state.Phase);
        }

        [TestMethod]
        public async Task PlayerLeaves_DuringTurn_Survival_AdvancesAndEliminates()
        {
            var (engine, state) = await StartGameAsync(playerCount: 3, survival: true);
            using var _ = state;
            var leaving = state.TurnManager.CurrentPlayer!;

            engine.HandlePlayerLeft(UserFactory.Create("dummy", leaving), state);

            Assert.IsTrue(state.GamePlayers[leaving].HasLeft);
            Assert.IsTrue(state.GamePlayers[leaving].IsEliminated);
            Assert.AreNotEqual(leaving, state.TurnManager.CurrentPlayer);
            // Two active players remain → the match continues.
            Assert.AreNotEqual(AlphaChainGamePhase.GameOver, state.Phase);
        }

        [TestMethod]
        public async Task PlayerLeaves_Survival_EndsGameWhenOneActivePlayerRemains()
        {
            var (engine, state) = await StartGameAsync(playerCount: 2, survival: true);
            using var _ = state;
            var leaving = state.TurnManager.CurrentPlayer!;

            engine.HandlePlayerLeft(UserFactory.Create("dummy", leaving), state);

            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.IsNotNull(state.Results);
        }

        // ── Leaves during Intermission ──────────────────────────────────────────

        // Starts a 3-player, EraInterval=1 game on a real dictionary chain and plays round 1
        // (cat → tea → ant) so the turn order wraps into the Intermission.
        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartAndEnterIntermissionAsync()
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService("cat", "tea", "ant"), new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < 3; i++)
                state.RegisterPlayer(MakePlayer(i));

            // One round per era, several eras so the Intermission runs (not the final round).
            state.UpdateSettings(s => s with { EraInterval = 1, EraCount = 3 });
            await engine.StartAsync(_host, state);
            state.Execute(() => state.BannedLetter = 'z');

            // Round 1: every player submits once → the order wraps → Intermission.
            await engine.SubmitWordAsync(state.TurnManager.TurnOrder[0], "cat", state);
            await engine.SubmitWordAsync(state.TurnManager.TurnOrder[1], "tea", state);
            await engine.SubmitWordAsync(state.TurnManager.TurnOrder[2], "ant", state);

            Assert.AreEqual(AlphaChainGamePhase.Intermission, state.Phase);
            return (engine, state);
        }

        // Ticks the FSM at t0 + `seconds`, advancing whichever timed sub-phase has elapsed.
        private static void TickAt(AlphaChainGameEngine engine, AlphaChainGameState state, DateTimeOffset t0, int seconds)
            => engine.Tick(state.Context!, t0.AddSeconds(seconds));

        [TestMethod]
        public async Task PlayerLeaves_DuringOptimization_DoesNotStallIntermission()
        {
            var (engine, state) = await StartAndEnterIntermissionAsync();
            using var _ = state;
            var t0 = DateTimeOffset.UtcNow;

            // Deal → Expansion → Optimization.
            TickAt(engine, state, t0, 10);
            TickAt(engine, state, t0, 20);
            Assert.AreEqual(IntermissionSubPhase.Optimization, state.IntermissionPhase);

            // A player drops mid-optimization.
            var leaving = state.TurnManager.TurnOrder[1];
            engine.HandlePlayerLeft(UserFactory.Create("dummy", leaving), state);
            Assert.IsTrue(state.GamePlayers[leaving].HasLeft);

            // The Intermission still completes on its timers (Optimization → SniperBan → done),
            // returning to the next era with a fresh banned letter.
            TickAt(engine, state, t0, 60);   // Optimization times out → SniperBan
            TickAt(engine, state, t0, 120);  // SniperBan times out → random draw → complete

            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.IsNotNull(state.BannedLetter);
            Assert.AreEqual(2, state.CurrentEra);
        }

        [TestMethod]
        public async Task PlayerLeaves_WhileHoldingSniperBan_FallsBackToTimeoutDraw()
        {
            var (engine, state) = await StartAndEnterIntermissionAsync();
            using var _ = state;
            var t0 = DateTimeOffset.UtcNow;

            // Advance all the way to the SniperBan sub-phase.
            TickAt(engine, state, t0, 10);   // → Expansion
            TickAt(engine, state, t0, 20);   // → Optimization
            TickAt(engine, state, t0, 60);   // → SniperBan
            Assert.AreEqual(IntermissionSubPhase.SniperBan, state.IntermissionPhase);

            // The resolved last-place picker leaves while holding the ban.
            var picker = state.SniperBanUserId!;
            Assert.IsNotNull(picker);
            engine.HandlePlayerLeft(UserFactory.Create("dummy", picker), state);
            Assert.IsTrue(state.GamePlayers[picker].HasLeft);

            // With the picker gone, the SniperBan timer's fallback draw resolves the letter and
            // the game proceeds — it never hangs waiting on a departed picker.
            TickAt(engine, state, t0, 120);

            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.IsNotNull(state.BannedLetter);
        }
    }
}
