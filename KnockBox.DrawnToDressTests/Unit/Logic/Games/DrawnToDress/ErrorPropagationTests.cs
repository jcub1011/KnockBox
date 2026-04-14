using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDress.Tests.Unit.Logic.Games.DrawnToDress
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
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

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
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act
            var result = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", "<svg>valid drawing</svg>"));

            // Assert
            Assert.IsTrue(result.IsSuccess);
        }

        [TestMethod]
        public async Task ProcessCommand_UnknownPlayer_ClaimPoolItem_DoesNotClaimItem()
        {
            // Arrange
            var (state, context) = await CreateGameInOutfitBuildingPhaseAsync();

            var item = new DrawnClothingItem
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p1",
                SvgContent = "<svg>hat</svg>",
                IsInPool = true,
            };
            context.ClothingPool[item.Id] = item;

            // Act: unknown player tries to claim an item.
            _engine.ProcessCommand(context,
                new ClaimPoolItemCommand("unknown-player", item.Id));

            // Assert: the item was not claimed.
            Assert.IsNull(item.ClaimedByPlayerId);
        }

        [TestMethod]
        public async Task ProcessCommand_SubmitDrawing_InvalidClothingType_DoesNotAddToPool()
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
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act: submit a drawing for a clothing type that doesn't exist.
            _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "nonexistent-type", "<svg>drawing</svg>"));

            // Assert: no item was added to the pool for the invalid type.
            Assert.DoesNotContain(i => i.ClothingTypeId == "nonexistent-type", context.ClothingPool.Values);
        }

        [TestMethod]
        public async Task ProcessCommand_SubmitCustomization_DuringDrawingPhase_IsIgnored()
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
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new DrawnToDressPlayerState { PlayerId = "p1" };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act: attempt to submit a customization while still in the drawing phase.
            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "My Outfit"));

            // Assert: the command was ignored (wrong state), player not marked ready.
            Assert.IsFalse(state.GamePlayers["p1"].IsReady);
        }
    }
}
