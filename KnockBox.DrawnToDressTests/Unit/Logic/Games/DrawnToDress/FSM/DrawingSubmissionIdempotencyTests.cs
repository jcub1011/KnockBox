using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress.FSM
{
    [TestClass]
    public class DrawingSubmissionIdempotencyTests
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
            _host = new User("Host", "host1");

            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object,
                _randomMock.Object);
        }

        private async Task<(DrawnToDressGameState state, DrawnToDressGameContext context)> CreateGameInDrawingPhaseAsync()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 10 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Add a player to the game.
            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };
            state.GamePlayers["p2"] = new DrawnToDressPlayerState { PlayerId = "p2" };

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            return (state, context);
        }

        [TestMethod]
        public async Task SubmitDrawing_DuplicateSvgFromSamePlayer_IsRejected()
        {
            // Arrange
            var (state, context) = await CreateGameInDrawingPhaseAsync();
            var svgContent = "<svg>test drawing</svg>";

            // Act: first submission should succeed.
            var firstResult = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", svgContent));

            // Act: second identical submission should fail.
            var secondResult = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", svgContent));

            // Assert
            Assert.IsTrue(firstResult.IsSuccess);
            Assert.IsTrue(secondResult.IsFailure);
        }

        [TestMethod]
        public async Task SubmitDrawing_DifferentSvgFromSamePlayer_IsAccepted()
        {
            // Arrange
            var (state, context) = await CreateGameInDrawingPhaseAsync();

            // Act
            var firstResult = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", "<svg>drawing1</svg>"));
            var secondResult = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", "<svg>drawing2</svg>"));

            // Assert
            Assert.IsTrue(firstResult.IsSuccess);
            Assert.IsTrue(secondResult.IsSuccess);
        }

        [TestMethod]
        public async Task SubmitDrawing_SameSvgFromDifferentPlayers_IsAccepted()
        {
            // Arrange
            var (state, context) = await CreateGameInDrawingPhaseAsync();
            var svgContent = "<svg>shared drawing</svg>";

            // Act
            var firstResult = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", svgContent));
            var secondResult = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p2", "hat", svgContent));

            // Assert
            Assert.IsTrue(firstResult.IsSuccess);
            Assert.IsTrue(secondResult.IsSuccess);
        }
    }
}
