using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Tests for <see cref="DrawnToDressGameContext"/> helper methods and
    /// FSM state transitions via direct context construction (no DI required).
    /// </summary>
    [TestClass]
    public class DrawnToDressGameContextTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _host = new User("Host", "host-id");
        }

        private DrawnToDressGameContext CreateContext(DrawnToDressSettings? settings = null)
        {
            using var _ = new DrawnToDressGameState(_host, _stateLoggerMock.Object, settings);
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object, settings);
            return new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);
        }

        // ── CalculateSwissRounds ──────────────────────────────────────────────

        [TestMethod]
        public void CalculateSwissRounds_OneOutfit_ReturnsOne()
        {
            Assert.AreEqual(1, DrawnToDressGameContext.CalculateSwissRounds(1));
        }

        [TestMethod]
        public void CalculateSwissRounds_FourOutfits_ReturnsTwo()
        {
            // ceil(log2(4)) = 2
            Assert.AreEqual(2, DrawnToDressGameContext.CalculateSwissRounds(4));
        }

        [TestMethod]
        public void CalculateSwissRounds_EightOutfits_ReturnsThree()
        {
            // ceil(log2(8)) = 3
            Assert.AreEqual(3, DrawnToDressGameContext.CalculateSwissRounds(8));
        }

        [TestMethod]
        public void CalculateSwissRounds_FiveOutfits_ReturnsThree()
        {
            // ceil(log2(5)) = 3
            Assert.AreEqual(3, DrawnToDressGameContext.CalculateSwissRounds(5));
        }

        // ── ResolveCriterionPoints ────────────────────────────────────────────

        [TestMethod]
        public void ResolveCriterionPoints_ALeads_AGetsFull()
        {
            var ctx = CreateContext();
            var (a, b) = ctx.ResolveCriterionPoints(votesA: 3, votesB: 1, weight: 5);
            Assert.AreEqual(5, a);
            Assert.AreEqual(0, b);
        }

        [TestMethod]
        public void ResolveCriterionPoints_BLeads_BGetsFullWeight()
        {
            var ctx = CreateContext();
            var (a, b) = ctx.ResolveCriterionPoints(votesA: 0, votesB: 4, weight: 7);
            Assert.AreEqual(0, a);
            Assert.AreEqual(7, b);
        }

        [TestMethod]
        public void ResolveCriterionPoints_TieFlipA_AWins()
        {
            _randomMock.Setup(r => r.GetRandomInt(0, 2, RandomType.Fast)).Returns(0); // 0 = A wins
            var ctx = CreateContext();
            var (a, b) = ctx.ResolveCriterionPoints(votesA: 2, votesB: 2, weight: 5);
            Assert.AreEqual(5, a);
            Assert.AreEqual(0, b);
        }

        [TestMethod]
        public void ResolveCriterionPoints_TieFlipB_BWins()
        {
            _randomMock.Setup(r => r.GetRandomInt(0, 2, RandomType.Fast)).Returns(1); // 1 = B wins
            var ctx = CreateContext();
            var (a, b) = ctx.ResolveCriterionPoints(votesA: 2, votesB: 2, weight: 5);
            Assert.AreEqual(0, a);
            Assert.AreEqual(5, b);
        }

        // ── GetOrCreatePendingOutfit ──────────────────────────────────────────

        [TestMethod]
        public void GetOrCreatePendingOutfit_CreatesNewOutfit()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            state.SetCurrentOutfitRound(1);
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);

            var outfit = ctx.GetOrCreatePendingOutfit("p1", "Player1");

            Assert.IsNotNull(outfit);
            Assert.AreEqual("p1", outfit.PlayerId);
            Assert.AreEqual(1, outfit.OutfitNumber);
        }

        [TestMethod]
        public void GetOrCreatePendingOutfit_ReturnsSameInstance()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            state.SetCurrentOutfitRound(1);
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);

            var first = ctx.GetOrCreatePendingOutfit("p1", "Player1");
            var second = ctx.GetOrCreatePendingOutfit("p1", "Player1");

            Assert.AreSame(first, second);
        }

        // ── DrawingState direct transition test ───────────────────────────────

        [TestMethod]
        public void DrawingState_NonHostAdvance_ReturnsError()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);
            var drawingState = new DrawingState();
            drawingState.OnEnter(ctx);

            var result = drawingState.HandleCommand(ctx, new AdvanceDrawingRoundCommand("non-host-id"));

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public void DrawingState_HostAdvance_WhenNotLastType_ReturnsNull()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);
            var drawingState = new DrawingState();
            drawingState.OnEnter(ctx);

            // Hat is not the last type (Hat → Shirt → Pants → Shoes)
            var result = drawingState.HandleCommand(ctx, new AdvanceDrawingRoundCommand(_host.Id));

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsNull(result.Value); // stays in DrawingState (advanced index)
            Assert.AreEqual(ClothingType.Shirt, state.CurrentDrawingType);
        }

        [TestMethod]
        public void DrawingState_HostAdvance_WhenLastType_TransitionsToOutfitBuilding()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);
            var drawingState = new DrawingState();
            drawingState.OnEnter(ctx);

            // Advance past all 4 types (hat=0, shirt=1, pants=2, shoes=3 → shoes is last)
            for (int i = 0; i < state.Settings.ClothingTypes.Count - 1; i++)
                state.AdvanceDrawingType();

            var result = drawingState.HandleCommand(ctx, new AdvanceDrawingRoundCommand(_host.Id));

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsInstanceOfType(result.Value, typeof(OutfitBuildingState));
        }

        // ── AwardTournamentBonus ──────────────────────────────────────────────

        [TestMethod]
        public void AwardTournamentBonus_SoleLeader_GetsBonus()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            state.GetOrAddPlayerScore("p1", "Player1").TotalPoints = 10;
            state.GetOrAddPlayerScore("p2", "Player2").TotalPoints = 5;
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);

            ctx.AwardTournamentBonus();

            Assert.AreEqual(10 + state.Settings.TournamentWinBonus, state.PlayerScores["p1"].TotalPoints);
            Assert.AreEqual(5, state.PlayerScores["p2"].TotalPoints);
        }

        [TestMethod]
        public void AwardTournamentBonus_Tied_NoBonusAwarded()
        {
            var state = new DrawnToDressGameState(_host, _stateLoggerMock.Object);
            state.GetOrAddPlayerScore("p1", "P1").TotalPoints = 10;
            state.GetOrAddPlayerScore("p2", "P2").TotalPoints = 10;
            var ctx = new DrawnToDressGameContext(state, _randomMock.Object, _loggerMock.Object);

            ctx.AwardTournamentBonus();

            // Both tied → neither gets the bonus
            Assert.AreEqual(10, state.PlayerScores["p1"].TotalPoints);
            Assert.AreEqual(10, state.PlayerScores["p2"].TotalPoints);
        }
    }
}
