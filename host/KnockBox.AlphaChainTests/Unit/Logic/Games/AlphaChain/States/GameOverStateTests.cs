using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Verifies the terminal <see cref="GameOverState"/> ranking rules: active players by score
    /// (descending), eliminated players ranked last, deterministic tie-breaking, and the survival
    /// last-player-standing winner.
    /// </summary>
    [TestClass]
    public class GameOverStateTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", Guid.NewGuid());
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", Guid.NewGuid());

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            int playerCount, bool survival = false)
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService(), new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            if (survival)
                state.UpdateSettings(s => s with { SurvivalMode = true });

            await engine.StartAsync(_host, state);
            return (engine, state);
        }

        private static void EnterGameOver(AlphaChainGameState state)
            => state.Execute(() => state.Context!.Fsm.TransitionTo(state.Context, new GameOverState()));

        [TestMethod]
        public async Task Rankings_OrderedByScoreDescending()
        {
            var (_, state) = await StartGameAsync(3);
            using var _ = state;
            var order = state.TurnManager.TurnOrder;
            state.Execute(() =>
            {
                state.GamePlayers[order[0]].Score = 10;
                state.GamePlayers[order[1]].Score = 30;
                state.GamePlayers[order[2]].Score = 20;
            });

            EnterGameOver(state);

            var ranks = state.Results!.Rankings;
            Assert.AreEqual(order[1], ranks[0].UserId);
            Assert.AreEqual(order[2], ranks[1].UserId);
            Assert.AreEqual(order[0], ranks[2].UserId);
            Assert.AreEqual(order[1], state.Results.WinnerUserId);
        }

        [TestMethod]
        public async Task Rankings_TieOnScore_BreaksByEarliestTurnOrder()
        {
            var (_, state) = await StartGameAsync(3);
            using var _ = state;
            var order = state.TurnManager.TurnOrder;
            state.Execute(() =>
            {
                foreach (var id in order) state.GamePlayers[id].Score = 15; // all tied
            });

            EnterGameOver(state);

            var ranks = state.Results!.Rankings;
            CollectionAssert.AreEqual(order.ToList(), ranks.Select(r => r.UserId).ToList());
            Assert.AreEqual(order[0], state.Results.WinnerUserId);
        }

        [TestMethod]
        public async Task Survival_LastSurvivorWins_RegardlessOfScore()
        {
            var (_, state) = await StartGameAsync(3, survival: true);
            using var _ = state;
            var order = state.TurnManager.TurnOrder;
            state.Execute(() =>
            {
                // The two eliminated players outscore the lone survivor.
                state.GamePlayers[order[0]].Score = 100;
                state.GamePlayers[order[1]].Score = 80;
                state.GamePlayers[order[2]].Score = 5; // survivor
                state.MarkEliminated(state.GamePlayers[order[0]]);
                state.MarkEliminated(state.GamePlayers[order[1]]);
            });

            EnterGameOver(state);

            // Survivor ranks first and wins despite the lowest score.
            Assert.AreEqual(order[2], state.Results!.Rankings[0].UserId);
            Assert.AreEqual(order[2], state.Results.WinnerUserId);
            // Eliminated players rank last, last-out (order[1]) above first-out (order[0]).
            Assert.AreEqual(order[1], state.Results.Rankings[1].UserId);
            Assert.AreEqual(order[0], state.Results.Rankings[2].UserId);
        }

        [TestMethod]
        public async Task Results_CaptureWordsPlayedAndTotals()
        {
            var (engine, state) = await StartGameAsync(2);
            using var _ = state;
            // One accepted play attributed to the first player via the engine path would be ideal,
            // but here we seed the play log directly to keep the test focused on aggregation.
            var first = state.TurnManager.TurnOrder[0];
            state.Execute(() =>
            {
                state.SubmissionHistory = state.SubmissionHistory
                    .Add(new KnockBox.AlphaChain.Services.State.Games.Data.AlphaChainSubmission(
                        DateTimeOffset.UtcNow, first, "Player0", "cat", 3, false, 0,
                        new KnockBox.AlphaChain.Services.Logic.Scoring.ScoreBreakdown("cat", 3, [], 3, false, 3)))
                    .Add(new KnockBox.AlphaChain.Services.State.Games.Data.AlphaChainSubmission(
                        DateTimeOffset.UtcNow, first, "Player0", "tap", 3, false, 0,
                        new KnockBox.AlphaChain.Services.Logic.Scoring.ScoreBreakdown("tap", 3, [], 3, false, 3)));
            });

            EnterGameOver(state);

            Assert.AreEqual(2, state.Results!.TotalWordsPlayed);
            var firstRow = state.Results.Rankings.First(r => r.UserId == first);
            Assert.AreEqual(2, firstRow.WordsPlayed);
        }
    }
}
