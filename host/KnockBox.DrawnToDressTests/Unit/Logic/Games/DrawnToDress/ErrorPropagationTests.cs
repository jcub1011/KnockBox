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
            _host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));

            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object,
                _randomMock.Object);
        }

        private async Task<(DrawnToDressGameState state, DrawnToDressGameContext context)> CreateGameInOutfitBuildingPhaseAsync()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.UpdateSettings(s => s with { ClothingTypes = [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ] });
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Add a player so we can advance through ready.
            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111") };

            // Mark ready to advance through drawing → pool reveal.
            _engine.ProcessCommand(context, new MarkReadyCommand(Guid.Parse("11111111-1111-1111-1111-111111111111")));
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
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SvgContent = "<svg>hat</svg>",
                IsInPool = true,
            };
            context.ClothingPool[item.Id] = item;

            // Act: player tries to claim their own item.
            var result = _engine.ProcessCommand(context,
                new ClaimPoolItemCommand(Guid.Parse("11111111-1111-1111-1111-111111111111"), item.Id));

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
            state.UpdateSettings(s => s with { ClothingTypes = [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 10 },
            ] });
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act
            var result = _engine.ProcessCommand(context,
                new SubmitDrawingCommand(Guid.Parse("11111111-1111-1111-1111-111111111111"), ClothingType.Hat, "<svg>valid drawing</svg>"));

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
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                SvgContent = "<svg>hat</svg>",
                IsInPool = true,
            };
            context.ClothingPool[item.Id] = item;

            // Act: unknown player tries to claim an item.
            _engine.ProcessCommand(context,
                new ClaimPoolItemCommand(Guid.NewGuid(), item.Id));

            // Assert: the item was not claimed.
            Assert.IsNull(item.ClaimedByPlayerId);
        }

        [TestMethod]
        public async Task ProcessCommand_SubmitDrawing_InvalidClothingType_DoesNotAddToPool()
        {
            // Arrange: create a game in the drawing phase.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.UpdateSettings(s => s with { ClothingTypes = [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 10 },
            ] });
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act: submit a drawing for a clothing type that isn't in the config.
            _engine.ProcessCommand(context,
                new SubmitDrawingCommand(Guid.Parse("11111111-1111-1111-1111-111111111111"), ClothingType.Top, "<svg>drawing</svg>"));

            // Assert: no item was added to the pool for the unconfigured type.
            Assert.DoesNotContain(i => i.ClothingTypeId == ClothingType.Top, context.ClothingPool.Values);
        }

        [TestMethod]
        public async Task ProcessCommand_SubmitCustomization_DuringDrawingPhase_IsIgnored()
        {
            // Arrange: create a game in the drawing phase.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.UpdateSettings(s => s with { ClothingTypes = [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 10 },
            ] });
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new DrawnToDressPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Act: attempt to submit a customization while still in the drawing phase.
            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand(Guid.Parse("11111111-1111-1111-1111-111111111111"), "My Outfit"));

            // Assert: the command was ignored (wrong state), player not marked ready.
            Assert.IsFalse(state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].IsReady);
        }
    }
}
