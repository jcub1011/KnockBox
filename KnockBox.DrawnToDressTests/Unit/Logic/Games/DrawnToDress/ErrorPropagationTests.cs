using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress
{
    [TestClass]
    public class ErrorPropagationTests
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

        private async Task<(DrawnToDressGameState state, DrawnToDressGameContext context)> CreateGameInOutfitBuildingPhaseAsync()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Add a player so we can advance through ready.
            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };

            // Mark ready to advance through drawing → pool reveal.
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);

            // Tick past pool reveal → outfit building.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);

            return (state, context);
        }

        [TestMethod]
        public async Task ProcessCommand_ClaimOwnItem_ReturnsErrorWithPlayerFacingMessage()
        {
            // Arrange
            var (state, context) = await CreateGameInOutfitBuildingPhaseAsync();

            // Create an item drawn by p1 and place it in the pool.
            var item = new DrawnClothingItem
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p1",
                SvgContent = "<svg>hat</svg>",
                IsInPool = true,
            };
            context.ClothingPool[item.Id] = item;

            // Act: player tries to claim their own item.
            var result = _engine.ProcessCommand(context,
                new ClaimPoolItemCommand("p1", item.Id));

            // Assert
            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var error));
            Assert.IsTrue(error.PublicMessage.Contains("claim", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public async Task ProcessCommand_ValidDrawingSubmission_ReturnsSuccess()
        {
            // Arrange: create a game in the drawing phase.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 10 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act
            var result = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", "<svg>valid drawing</svg>"));

            // Assert
            Assert.IsTrue(result.IsSuccess);
        }
    }
}
