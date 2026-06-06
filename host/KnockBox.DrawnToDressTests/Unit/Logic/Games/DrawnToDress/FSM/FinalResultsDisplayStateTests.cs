using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDress.Tests.Unit.Logic.Games.DrawnToDress.FSM
{
    [TestClass]
    public class FinalResultsDisplayStateTests
    {
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private Mock<IRandomNumberService> _randomMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object,
                _randomMock.Object);
        }

        private async Task<(DrawnToDressGameState state, DrawnToDressGameContext context)> CreateGameAsync()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            state.GamePlayers[Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")] = new() { PlayerId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"), DisplayName = "Player A" };
            state.GamePlayers[Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB")] = new() { PlayerId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"), DisplayName = "Player B" };
            return (state, state.Context!);
        }

        [TestMethod]
        public async Task OnEnter_WithoutPriorFlips_ShowsLeaderboardAsIs()
        {
            var (state, context) = await CreateGameAsync();
            state.Leaderboard =
            [
                new LeaderboardEntry { PlayerId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"), DisplayName = "Player A", TotalScore = 10, Rank = 1 },
                new LeaderboardEntry { PlayerId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"), DisplayName = "Player B", TotalScore = 5, Rank = 2 },
            ];
            state.PendingCoinFlipQueue = [];

            context.Fsm.TransitionTo(context, new FinalResultsDisplayState());

            Assert.IsInstanceOfType<FinalResultsDisplayState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Results, state.Phase);
            Assert.HasCount(2, state.Leaderboard);
            Assert.AreEqual(1, state.Leaderboard[0].Rank);
        }

        [TestMethod]
        public async Task OnEnter_AfterCoinFlip_ReRanksEntries()
        {
            var (state, context) = await CreateGameAsync();
            state.Leaderboard =
            [
                new LeaderboardEntry { PlayerId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"), DisplayName = "Player A", TotalScore = 10, MatchupWins = 2, Rank = 1 },
                new LeaderboardEntry { PlayerId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"), DisplayName = "Player B", TotalScore = 10, MatchupWins = 2, Rank = 1 },
            ];
            state.PendingCoinFlipQueue =
            [
                new PendingCoinFlipEntry
                {
                    Context = CoinFlipContext.FinalStandingsTie,
                    PlayerAId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"),
                    PlayerBId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"),
                    CallerPlayerId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"),
                    CallerChoseHeads = true,
                    ResultIsHeads = true,
                    WinnerPlayerId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"),
                    IsResolved = true,
                }
            ];

            context.Fsm.TransitionTo(context, new FinalResultsDisplayState());

            Assert.IsInstanceOfType<FinalResultsDisplayState>(context.Fsm.CurrentState);
            // Winner should have a better rank.
            var winnerEntry = state.Leaderboard.First(e => e.PlayerId == Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"));
            var loserEntry = state.Leaderboard.First(e => e.PlayerId == Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"));
            Assert.IsLessThan(loserEntry.Rank, winnerEntry.Rank,
                "Coin flip winner should have a better (lower) rank.");
            Assert.AreEqual("coin_flip", winnerEntry.TiebreakMethod);
            Assert.AreEqual("coin_flip", loserEntry.TiebreakMethod);
        }

        [TestMethod]
        public async Task PlayAgainCommand_FromHost_TransitionsToLobbyState()
        {
            var (state, context) = await CreateGameAsync();
            state.Leaderboard = [];
            state.PendingCoinFlipQueue = [];

            context.Fsm.TransitionTo(context, new FinalResultsDisplayState());
            _engine.ProcessCommand(context, new PlayAgainCommand(Guid.Parse("00000000-0000-0000-0000-000000000001")));

            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task PlayAgainCommand_FromNonHost_IsRejected()
        {
            var (state, context) = await CreateGameAsync();
            state.Leaderboard = [];
            state.PendingCoinFlipQueue = [];

            context.Fsm.TransitionTo(context, new FinalResultsDisplayState());
            _engine.ProcessCommand(context, new PlayAgainCommand(Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")));

            Assert.IsInstanceOfType<FinalResultsDisplayState>(context.Fsm.CurrentState);
        }
    }
}
