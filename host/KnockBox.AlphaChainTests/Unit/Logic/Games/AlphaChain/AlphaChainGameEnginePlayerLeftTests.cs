using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
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
                new StubWordListService(), new FixedRandomNumberService(),
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
    }
}
