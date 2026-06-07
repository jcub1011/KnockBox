using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.PlayLog;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Verifies <see cref="AlphaChainPlayLogMetadata.Build"/> maps the terminal
    /// <see cref="KnockBox.AlphaChain.Services.State.Games.Data.GameResults"/> into the
    /// expected match-level and personal metadata pairs.
    /// </summary>
    [TestClass]
    public class AlphaChainPlayLogMetadataTests
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

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(int playerCount)
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService(), new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(UserFactory.Create($"Player{i}", Guid.NewGuid()));

            await engine.StartAsync(_host, state);
            return (engine, state);
        }

        private static void EnterGameOver(AlphaChainGameState state)
            => state.Execute(() => state.Context!.Fsm.TransitionTo(state.Context, new GameOverState()));

        [TestMethod]
        public async Task Build_ForWinner_EmitsMatchAndPersonalKeys()
        {
            var (_, state) = await StartGameAsync(3);
            using var _ = state;
            var order = state.TurnManager.TurnOrder;
            state.Execute(() =>
            {
                state.GamePlayers[order[0]].Score = 10;
                state.GamePlayers[order[1]].Score = 30; // top score -> winner, rank 1
                state.GamePlayers[order[2]].Score = 20;
            });

            EnterGameOver(state);

            var winnerId = state.Results!.WinnerUserId;
            var expectedWinnerName = state.Results.Rankings[0].DisplayName;
            var metadata = AlphaChainPlayLogMetadata.Build(state, winnerId);

            // Match-level keys.
            Assert.AreEqual(winnerId, order[1], "Top score should win.");
            Assert.AreEqual(expectedWinnerName, metadata["Winner"]);
            Assert.AreEqual("3", metadata["Players"]);
            Assert.AreEqual(state.Results.TotalWordsPlayed.ToString(), metadata["Total Words"]);
            Assert.AreEqual(state.Results.Duration.ToString(@"mm\:ss"), metadata["Duration"]);

            // Personal keys for the winner.
            Assert.AreEqual("Won", metadata["Result"]);
            Assert.AreEqual("1 / 3", metadata["Placement"]);
            Assert.AreEqual("30", metadata["Score"]);
            Assert.AreEqual("0", metadata["Words Played"]);
        }

        [TestMethod]
        public async Task Build_ForNonWinningPlayer_ReportsPlacementAndSurvived()
        {
            var (_, state) = await StartGameAsync(3);
            using var _ = state;
            var order = state.TurnManager.TurnOrder;
            state.Execute(() =>
            {
                state.GamePlayers[order[0]].Score = 10;
                state.GamePlayers[order[1]].Score = 30;
                state.GamePlayers[order[2]].Score = 20; // middle -> rank 2 (placement "2 / 3")
            });

            EnterGameOver(state);

            var metadata = AlphaChainPlayLogMetadata.Build(state, order[2]);

            Assert.AreEqual("Survived", metadata["Result"]);
            Assert.AreEqual("2 / 3", metadata["Placement"]);
            Assert.AreEqual("20", metadata["Score"]);
        }

        [TestMethod]
        public async Task Build_ForNonPlayer_OmitsPersonalKeys()
        {
            var (_, state) = await StartGameAsync(2);
            using var _ = state;

            EnterGameOver(state);

            var metadata = AlphaChainPlayLogMetadata.Build(state, Guid.NewGuid());

            // Match-level keys still present, personal keys absent.
            Assert.IsTrue(metadata.ContainsKey("Winner"));
            Assert.AreEqual("2", metadata["Players"]);
            Assert.IsFalse(metadata.ContainsKey("Result"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
            Assert.IsFalse(metadata.ContainsKey("Score"));
            Assert.IsFalse(metadata.ContainsKey("Words Played"));
        }
    }
}
