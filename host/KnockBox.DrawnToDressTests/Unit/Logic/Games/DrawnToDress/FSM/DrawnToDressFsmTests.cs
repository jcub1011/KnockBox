using KnockBox.Core.Services.State.Games.Shared;
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
    /// <summary>
    /// Tests FSM creation, state entry, and nominal phase transitions for Drawn To Dress.
    /// </summary>
    [TestClass]
    public class DrawnToDressFsmTests
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

        // ── FSM creation ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task CreateStateAsync_InitializesFsmInLobbyState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (DrawnToDressGameState)result.Value!;
            Assert.IsNotNull(state.Context);
            Assert.IsNotNull(state.Context.Fsm);
            Assert.IsInstanceOfType<LobbyState>(state.Context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task StartAsync_TransitionsFromLobbyToThemeSelection()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)startResult.IsSuccess);
            // Theme selection is the first phase after lobby.
            Assert.AreEqual(GamePhase.ThemeSelection, state.Phase);
            Assert.IsInstanceOfType<ThemeSelectionState>(state.Context!.Fsm.CurrentState);
            Assert.IsNotNull(state.PhaseDeadlineUtc);

            // After ticking past the announcement time, it should advance to Drawing.
            _engine.Tick(state.Context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.IsInstanceOfType<DrawingRoundState>(state.Context.Fsm.CurrentState);
        }

        // ── Command dispatch ──────────────────────────────────────────────────

        [TestMethod]
        public async Task ProcessCommand_UnknownCommand_DoesNotTransition()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            var initialState = context.Fsm.CurrentState;

            // MarkReadyCommand is not handled in LobbyState → should be a no-op.
            _engine.ProcessCommand(context, new MarkReadyCommand(_host.Id));

            Assert.AreSame(initialState, context.Fsm.CurrentState);
        }

        // ── Nominal phase flow ────────────────────────────────────────────────

        [TestMethod]
        public async Task DrawingRoundState_OnTimerExpiry_TransitionsToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            // Use a single clothing type so the first (and only) round's timer goes
            // directly to PoolReveal → OutfitBuilding.
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Simulate timer expiry by ticking far into the future.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Drawing timer ended → now in PoolRevealState.
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);

            // Advance through the pool reveal timer.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task DrawingRoundState_AllPlayersReady_TransitionsEarly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            // Use a single clothing type so all-ready skips straight to OutfitBuilding.
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Register a player and mark them ready.
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            // All one player is ready in the last (only) drawing round → transitions to PoolReveal.
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);

            // Advance through the pool reveal timer to reach outfit building.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        // ── Sequential drawing rounds ─────────────────────────────────────────

        [TestMethod]
        public async Task DrawingRoundState_TimerExpiry_AdvancesToNextClothingType()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat,  DisplayName = "Hat",  MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top,  DisplayName = "Top",  MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Shoes, DisplayName = "Shoes", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(0, state.CurrentDrawingClothingTypeIndex);

            // Tick past first round — should advance to round 1 (Top), still Drawing.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);
        }

        [TestMethod]
        public async Task DrawingRoundState_LastType_TimerExpiry_TransitionsToPoolReveal_ThenOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat,  DisplayName = "Hat",  MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top,  DisplayName = "Top",  MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Advance through hat round.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);

            // Advance through top (last) round → PoolReveal.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);

            // Advance through the pool reveal timer → OutfitBuilding.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task DrawingRoundState_AllReady_AdvancesToNextClothingType()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top, DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            Assert.AreEqual(0, state.CurrentDrawingClothingTypeIndex);

            // Mark ready on the first round → should advance to round 1 (Top).
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);
        }

        [TestMethod]
        public async Task DrawingRoundState_AllReady_LastType_TransitionsToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top, DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            // Advance past hat round.
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);

            // Mark ready on last round → PoolReveal.
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);

            // Advance through the pool reveal timer → OutfitBuilding.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task DrawingRoundState_TracksCurrentClothingTypeIndex()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat,    DisplayName = "Hat",    MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top,    DisplayName = "Top",    MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Bottom, DisplayName = "Bottom", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.AreEqual(0, state.CurrentDrawingClothingTypeIndex);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.AreEqual(2, state.CurrentDrawingClothingTypeIndex);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            // All types done — transitioned to PoolReveal.
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);

            // Advance through pool reveal → OutfitBuilding.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task DrawingRoundState_SubmitDrawing_WrongType_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top, DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            // Currently in hat round; submitting "top" should be ignored.
            _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", ClothingType.Top, "<svg/>"));

            Assert.IsEmpty(state.ClothingPool, "Wrong-type submission must be discarded.");
        }

        [TestMethod]
        public async Task DrawingRoundState_MaxItemsPerType_RejectsExcessSubmissions()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 2 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            // Submit up to the max.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>1</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>2</svg>"));
            Assert.HasCount(2, state.ClothingPool);

            // Third submission should be rejected.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>3</svg>"));
            Assert.HasCount(2, state.ClothingPool, "Submission beyond MaxItemsPerRound must be discarded.");
        }

        [TestMethod]
        public async Task DrawingRoundState_PlayerDrawsNothing_TimerAdvancesRound()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Player draws nothing — pool stays empty.
            Assert.IsEmpty(state.ClothingPool);

            // Timer still advances the game → PoolReveal.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.IsEmpty(state.ClothingPool, "Pool must remain empty when nothing was drawn.");

            // Advance through pool reveal timer → OutfitBuilding.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.IsEmpty(state.ClothingPool, "Pool must remain empty when nothing was drawn.");
        }

        [TestMethod]
        public async Task DrawingRoundState_SubmitDrawing_StoredWithCreatorAttribution()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>my hat</svg>"));

            Assert.HasCount(1, state.ClothingPool);
            var item = state.ClothingPool.Values.Single();
            Assert.AreEqual("p1", item.CreatorPlayerId);
            Assert.AreEqual(ClothingType.Hat, item.ClothingTypeId);
            Assert.AreEqual("<svg>my hat</svg>", item.SvgContent);
            Assert.IsTrue(item.IsInPool);
        }

        [TestMethod]
        public async Task DrawingRoundState_MultiplePlayersEachType_AllItemsStoredInPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>p1 hat</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", ClothingType.Hat, "<svg>p2 hat</svg>"));

            Assert.HasCount(2, state.ClothingPool);
            Assert.IsTrue(state.ClothingPool.Values.All(i => i.ClothingTypeId == ClothingType.Hat));
            Assert.IsTrue(state.ClothingPool.Values.All(i => i.IsInPool));
        }

        [TestMethod]
        public async Task DrawingRoundState_ReadyFlagsResetBetweenRounds()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top, DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            // p1 marks ready in hat round.
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));
            // p2 is not ready yet, so we should still be in the hat round.
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(0, state.CurrentDrawingClothingTypeIndex);
            Assert.IsTrue(state.GamePlayers["p1"].IsReady);

            // p2 marks ready → hat round done, advances to top round.
            _engine.ProcessCommand(context, new MarkReadyCommand("p2"));
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);

            // Ready flags should have been reset for the new round.
            Assert.IsFalse(state.GamePlayers["p1"].IsReady, "IsReady must be cleared on entering a new round.");
            Assert.IsFalse(state.GamePlayers["p2"].IsReady, "IsReady must be cleared on entering a new round.");
        }

        // ── Pool reveal phase ─────────────────────────────────────────────────

        [TestMethod]
        public async Task PoolRevealState_OnTimerExpiry_TransitionsToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new PoolRevealState());
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);
            Assert.IsNotNull(state.PhaseDeadlineUtc);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task PoolRevealState_AllPlayersReady_AdvancesEarlyToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new PoolRevealState());
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);

            // One player ready — not all ready yet.
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.IsTrue(state.GamePlayers["p1"].IsReady);
            Assert.IsFalse(state.GamePlayers["p2"].IsReady);

            // Second player ready — all ready → advance immediately.
            _engine.ProcessCommand(context, new MarkReadyCommand("p2"));
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task PoolRevealState_SinglePlayer_MarkReady_AdvancesEarly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            context.Fsm.TransitionTo(context, new PoolRevealState());

            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task PoolRevealState_ReadyFlagsResetOnEntry()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1", IsReady = true };
            state.GamePlayers["p2"] = new() { PlayerId = "p2", IsReady = true };

            context.Fsm.TransitionTo(context, new PoolRevealState());

            Assert.IsFalse(state.GamePlayers["p1"].IsReady, "IsReady must be cleared on entering PoolRevealState.");
            Assert.IsFalse(state.GamePlayers["p2"].IsReady, "IsReady must be cleared on entering PoolRevealState.");
        }

        [TestMethod]
        public async Task PoolRevealState_ClaimPoolItem_IsRejectedViewOnly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new PoolRevealState());
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);

            // Attempt to claim during reveal — must be ignored.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState,
                "State must not change when a claim is attempted during pool reveal.");
            Assert.IsNull(state.ClothingPool[itemId].ClaimedByPlayerId,
                "Item must not be claimed during the pool reveal phase.");
        }

        [TestMethod]
        public async Task PoolRevealState_UnknownPlayer_MarkReady_IsIgnored()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new PoolRevealState());

            // No players registered; command from unknown player is a no-op.
            _engine.ProcessCommand(context, new MarkReadyCommand("unknown"));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task PoolRevealState_HasDeadlineSet()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.PoolRevealTimeSec = 45;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var before = DateTimeOffset.UtcNow;
            context.Fsm.TransitionTo(context, new PoolRevealState());
            var after = DateTimeOffset.UtcNow;

            Assert.IsNotNull(state.PhaseDeadlineUtc);
            Assert.IsTrue(state.PhaseDeadlineUtc >= before.AddSeconds(44),
                "Deadline should be roughly PoolRevealTimeSec in the future.");
            Assert.IsTrue(state.PhaseDeadlineUtc <= after.AddSeconds(46),
                "Deadline should be roughly PoolRevealTimeSec in the future.");
        }

        [TestMethod]
        public async Task PoolRevealState_DeadlineClearedOnExit()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.PoolRevealTimeSec = 30;
            state.Config.OutfitBuildingTimeSec = 90;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new PoolRevealState());
            var revealDeadline = state.PhaseDeadlineUtc;
            Assert.IsNotNull(revealDeadline);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // PoolRevealState cleared its deadline on exit; OutfitBuildingState then set a new one.
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.PhaseDeadlineUtc, "OutfitBuildingState should set a new deadline.");
            Assert.AreNotEqual(revealDeadline, state.PhaseDeadlineUtc,
                "Deadline should have changed when transitioning out of PoolRevealState.");
        }

        // ── Pool aggregation ──────────────────────────────────────────────────

        [TestMethod]
        public async Task PoolReveal_PoolContainsItemsFromMultipleRounds()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top, DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            // Submit hats in round 0.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>p1 hat</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", ClothingType.Hat, "<svg>p2 hat</svg>"));

            // Advance to top round.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);

            // Submit tops in round 1.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Top, "<svg>p1 top</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", ClothingType.Top, "<svg>p2 top</svg>"));

            // Advance through top round → PoolReveal.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.HasCount(4, state.ClothingPool, "Pool should contain items from all drawing rounds.");
            Assert.AreEqual(2, state.ClothingPool.Values.Count(i => i.ClothingTypeId == ClothingType.Hat),
                "Pool should contain 2 hat items.");
            Assert.AreEqual(2, state.ClothingPool.Values.Count(i => i.ClothingTypeId == ClothingType.Top),
                "Pool should contain 2 top items.");
        }

        [TestMethod]
        public async Task PoolReveal_AllItemsAreInPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>p1</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", ClothingType.Hat, "<svg>p2</svg>"));

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.IsTrue(state.ClothingPool.Values.All(i => i.IsInPool),
                "All pool items must have IsInPool = true during the reveal phase.");
        }

        [TestMethod]
        public async Task PoolReveal_ItemsGroupedByClothingType()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = ClothingType.Top, DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Hat, "<svg>hat</svg>"));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // hat → top
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", ClothingType.Top, "<svg>top</svg>"));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // top → PoolReveal

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);

            var byType = state.ClothingPool.Values
                .GroupBy(i => i.ClothingTypeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            Assert.IsTrue(byType.ContainsKey(ClothingType.Hat), "Pool should have items grouped under 'hat'.");
            Assert.IsTrue(byType.ContainsKey(ClothingType.Top), "Pool should have items grouped under 'top'.");
            Assert.HasCount(1, byType[ClothingType.Hat]);
            Assert.HasCount(1, byType[ClothingType.Top]);
        }

        [TestMethod]
        public async Task PoolReveal_ReadyCountUpdatesCorrectly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            state.GamePlayers["p3"] = new() { PlayerId = "p3" };

            context.Fsm.TransitionTo(context, new PoolRevealState());

            // Initially no one is ready.
            int ReadyCount() => state.GamePlayers.Values.Count(p => p.IsReady);
            Assert.AreEqual(0, ReadyCount());

            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));
            Assert.AreEqual(1, ReadyCount());

            _engine.ProcessCommand(context, new MarkReadyCommand("p2"));
            Assert.AreEqual(2, ReadyCount());

            // Still in PoolRevealState — not all ready yet.
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);

            _engine.ProcessCommand(context, new MarkReadyCommand("p3"));
            // Now all three are ready → advanced.
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task PoolRevealConfig_NormalizesInvalidTimeSec()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            var invalidConfig = new KnockBox.DrawnToDress.Services.State.Games.Data.DrawnToDressConfig
            {
                PoolRevealTimeSec = 1, // below minimum of 5
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand(_host.Id, invalidConfig));

            Assert.AreEqual(5, state.Config.PoolRevealTimeSec,
                "PoolRevealTimeSec must be clamped to the minimum of 5 seconds.");
        }

        [TestMethod]
        public async Task OutfitBuildingState_OnTimerExpiry_TransitionsToCustomization()        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Skip to OutfitBuildingState.
            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.Phase);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_OnTimerExpiry_TransitionsToPoolReveal()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // No players → no distinctness conflicts, so goes straight to PoolRevealState.
            context.Config.NumOutfitRounds = 2;
            context.Fsm.TransitionTo(context, new OutfitCustomizationState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);
        }

        [TestMethod]
        public async Task VotingMatchupState_OnTimerExpiry_TransitionsToRoundResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.VotingRoundResults, state.Phase);
        }

        [TestMethod]
        public async Task VotingRoundResultsState_AllRoundsComplete_TransitionsToFinalResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Set explicit round count and pre-fill all voting rounds so the timer expiry ends the game.
            state.Config.VotingRounds = 3;
            for (int i = 0; i < state.Config.VotingRounds; i++)
                state.VotingRounds.Add(new() { RoundNumber = i + 1 });

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            // Fast-forward past the auto-advance timer.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.IsInstanceOfType<FinalResultsDisplayState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Results, state.Phase);
        }

        [TestMethod]
        public async Task VotingRoundResultsState_MoreRoundsRemain_TransitionsToNextSetup()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Set explicit round count and add fewer than configured.
            // Add 2 rounds, so one more should remain.
            state.Config.VotingRounds = 3;
            state.VotingRounds.Add(new() { RoundNumber = 1 });
            state.VotingRounds.Add(new() { RoundNumber = 2 });

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            // Fast-forward past the auto-advance timer.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddMinutes(1));

            // VotingRoundSetupState chains immediately to VotingMatchupState.
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);
        }

        // ── Coin flip ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task CoinFlipState_OnEnter_TransitionsToRoundResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.VotingRounds.Add(new() { RoundNumber = 1 });
            context.Fsm.TransitionTo(context, new CoinFlipState(new VotingRoundResultsState()));

            // With an empty queue, CoinFlipState chains immediately to the return state.
            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.VotingRoundResults, state.Phase);
            // PendingCoinFlipMatchupId is cleared on exit from CoinFlipState.
            Assert.IsNull(state.PendingCoinFlipMatchupId);
        }

        // ── Pause / abandon ───────────────────────────────────────────────────

        [TestMethod]
        public async Task PauseGame_FromDrawingRound_TransitionsToPausedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Paused, state.Phase);
        }

        [TestMethod]
        public async Task ResumeGame_FromPaused_ReturnsToPreviousState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Pause from the drawing round.
            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);

            // Resume — should go back to DrawingRoundState.
            _engine.ProcessCommand(context, new ResumeGameCommand(_host.Id));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
        }

        [TestMethod]
        public async Task ResumeGame_ByNonHost_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);

            // Non-host attempts to resume.
            _engine.ProcessCommand(context, new ResumeGameCommand("nonhost_id"));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeSelectionState_HostPick_WaitsForSelectThemeCommand()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Force back to ThemeSelectionState with HostPick mode.
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.HostPick;
            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.ThemeSelection, state.Phase);

            // Now host selects a theme.
            _engine.ProcessCommand(context, new SelectThemeCommand(_host.Id, "retro_futurism"));

            // Should still be in ThemeSelectionState until announcement time expires.
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.AreEqual("retro_futurism", state.CurrentTheme?.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_Random_SelectsThemeAndWaitsForAnnouncement()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.Random;
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Random source → selects theme but waits for announcement time before advancing.
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsFalse(string.IsNullOrEmpty(state.CurrentTheme.Id));

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_WaitsForAllSubmissions()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);

            // Only the first player submits — should stay in ThemeSelectionState.
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Sci-Fi Noir"));
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.IsNull(state.CurrentTheme);
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_AllPlayersSubmit_SelectsThemeAndAdvances()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Sci-Fi Noir"));
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p2", "Medieval Fantasy"));

            // Both submitted → theme chosen, but still in ThemeSelectionState until announcement.
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.CurrentTheme);
            var validThemes = new[] { "Sci-Fi Noir", "Medieval Fantasy" };
            CollectionAssert.Contains(validThemes, state.CurrentTheme.Id);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_SubmissionsStoredInGameState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Cosmic Horror"));

            Assert.IsTrue(state.PlayerThemeSubmissions.ContainsKey("p1"));
            Assert.AreEqual("Cosmic Horror", state.PlayerThemeSubmissions["p1"]);
            Assert.IsFalse(state.PlayerThemeSubmissions.ContainsKey("p2"));
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_PopulatesCandidatesOnEntry()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.HasCount(3, state.ThemeCandidates);
            // Candidates should be distinct.
            var ids = state.ThemeCandidates.Select(t => t.Id).ToList();
            Assert.HasCount(ids.Count, ids.Distinct());
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_AllPlayersVote_SelectsWinnerAndAdvances()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            state.GamePlayers["p3"] = new() { PlayerId = "p3" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);

            var winningCandidate = state.ThemeCandidates[0];

            // Two players vote for the first candidate; one for another.
            _engine.ProcessCommand(context, new VoteForThemeCommand("p1", winningCandidate.Id));
            _engine.ProcessCommand(context, new VoteForThemeCommand("p2", winningCandidate.Id));
            _engine.ProcessCommand(context, new VoteForThemeCommand("p3", state.ThemeCandidates[1].Id));

            // All voted → winner selected, but still in ThemeSelectionState until announcement.
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.AreEqual(winningCandidate.Id, state.CurrentTheme?.Id);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_WaitsForAllVotes()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Only one player votes — should stay in ThemeSelectionState.
            _engine.ProcessCommand(context, new VoteForThemeCommand("p1", state.ThemeCandidates[0].Id));

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.IsNull(state.CurrentTheme);
        }

        // ── Announcement timing ───────────────────────────────────────────────

        [TestMethod]
        public async Task ThemeAnnouncement_BeforeDrawing_ThemeRevealedAfterSelection()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeAnnouncement.BeforeDrawing;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Theme should be revealed immediately in BeforeDrawing mode.
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsTrue(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeAnnouncement_AfterDrawing_ThemeNotRevealedDuringDrawing()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeAnnouncement.AfterDrawing;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Theme selected but NOT yet revealed to players.
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsFalse(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.IsFalse(state.ThemeRevealedToPlayers);
        }

        [TestMethod]
        public async Task ThemeAnnouncement_AfterDrawing_ThemeRevealedAfterDrawingCompletes()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.DrawnToDress.Services.State.Games.Data.ThemeAnnouncement.AfterDrawing;
            // Use a single clothing type so one tick exhausts the drawing phase.
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];

            // Enter ThemeSelectionState → auto-selects theme but does not reveal.
            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            Assert.IsFalse(state.ThemeRevealedToPlayers);

            // Tick past the announcement time → DrawingRoundState.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.IsFalse(state.ThemeRevealedToPlayers);

            // Simulate drawing timer expiry → PoolRevealState (theme revealed here).
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // After drawing completes (in PoolRevealState) the theme should be revealed.
            Assert.IsTrue(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);

            // Advance through the pool reveal timer → OutfitBuildingState.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        // ── Same theme for both outfits ────────────────────────────────────────

        [TestMethod]
        public async Task BothOutfits_SameThemeUsedThroughoutSession()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            // Default: Random source, BeforeDrawing.
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var selectedTheme = state.CurrentTheme;
            Assert.IsNotNull(selectedTheme, "Theme should be selected after game start.");

            // Advance through drawing and pool reveal.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // Drawing → PoolReveal
            Assert.AreEqual(selectedTheme, state.CurrentTheme,
                "Theme must remain the same after drawing phase.");

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // PoolReveal → OutfitBuilding
            Assert.AreEqual(selectedTheme, state.CurrentTheme,
                "Theme must remain the same during outfit building.");
        }

        // ── Outfit distinctness resolution ────────────────────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_WithConflict_GoesToDistinctnessResolution()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Set up two players sharing the same item.
            var sharedId = Guid.NewGuid();
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = sharedId },
                }
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new()
                {
                    PlayerId = "p2",
                    SelectedItemsByType = new() { [ClothingType.Hat] = sharedId },
                }
            };

            context.Config.NumOutfitRounds = 2;
            context.Fsm.TransitionTo(context, new OutfitCustomizationState());
            // Timer expires with all outfits already submitted and a conflict present.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitDistinctnessResolutionState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitDistinctnessResolution, state.Phase);
        }

        // ── Lobby: host permissions ───────────────────────────────────────────

        [TestMethod]
        public async Task LobbyState_UpdateConfigCommand_ByHost_UpdatesConfig()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            var updatedConfig = new KnockBox.DrawnToDress.Services.State.Games.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 240,
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand(_host.Id, updatedConfig));

            // Should remain in lobby and config should be updated.
            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(240, state.Config.DrawingTimeSec);
        }

        [TestMethod]
        public async Task LobbyState_UpdateConfigCommand_ByNonHost_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            int originalDrawingTime = state.Config.DrawingTimeSec;

            var updatedConfig = new KnockBox.DrawnToDress.Services.State.Games.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 999,
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand("nonhost_id", updatedConfig));

            // Config should not have changed.
            Assert.AreEqual(originalDrawingTime, state.Config.DrawingTimeSec);
        }

        [TestMethod]
        public async Task LobbyState_UpdateConfigCommand_NormalizesInvalidValues()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            var invalidConfig = new KnockBox.DrawnToDress.Services.State.Games.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 1,     // below minimum of 30
                VotingRounds = -1,      // below minimum of 0
                BonusPointsForCompleteOutfit = -10, // negative
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand(_host.Id, invalidConfig));

            Assert.AreEqual(30, state.Config.DrawingTimeSec);
            Assert.AreEqual(0, state.Config.VotingRounds);
            Assert.AreEqual(0, state.Config.BonusPointsForCompleteOutfit);
        }

        [TestMethod]
        public async Task LobbyState_StartGameCommand_ByNonHost_DoesNotTransition()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            _engine.ProcessCommand(context, new StartGameCommand("notthehost"));

            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        // ── Outfit building – claim/unclaim ───────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_ClaimPoolItem_SelfDrawnItem_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var itemId = Guid.NewGuid();
            // Item created BY p1 – p1 must not be able to claim it via ClaimPoolItemCommand.
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));

            Assert.IsNull(state.ClothingPool[itemId].ClaimedByPlayerId,
                "A player must not be able to claim an item they created.");
            Assert.DoesNotContain(itemId, state.GamePlayers["p1"].OwnedClothingItemIds,
                "Self-drawn items must not be added to OwnedClothingItemIds via ClaimPoolItemCommand.");
        }

        [TestMethod]
        public async Task OutfitBuilding_ClaimPoolItem_AlreadyClaimed_SecondClaimFails()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p3",      // drawn by a third player
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // p1 claims first.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));
            Assert.AreEqual("p1", state.ClothingPool[itemId].ClaimedByPlayerId);

            // p2 attempts to claim the same item – must be rejected.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));

            Assert.AreEqual("p1", state.ClothingPool[itemId].ClaimedByPlayerId,
                "First claim must win; subsequent claims must fail.");
            Assert.DoesNotContain(itemId, state.GamePlayers["p2"].OwnedClothingItemIds,
                "Losing claimer must not have the item in their owned list.");
        }

        [TestMethod]
        public async Task OutfitBuilding_ClaimPoolItem_Success_AddsToOwnedList()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",      // drawn by p1
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // p2 claims p1's hat – this is valid.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));

            Assert.AreEqual("p2", state.ClothingPool[itemId].ClaimedByPlayerId,
                "Claim by a different player must succeed.");
            Assert.Contains(itemId, state.GamePlayers["p2"].OwnedClothingItemIds,
                "Claimed item must appear in the claimer's OwnedClothingItemIds.");
        }

        [TestMethod]
        public async Task OutfitBuilding_UnclaimPoolItem_ReturnsToPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // p2 claims, then unclaims.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));
            Assert.AreEqual("p2", state.ClothingPool[itemId].ClaimedByPlayerId);

            _engine.ProcessCommand(context, new UnclaimPoolItemCommand("p2", itemId));

            Assert.IsNull(state.ClothingPool[itemId].ClaimedByPlayerId,
                "Unclaimed item must have its ClaimedByPlayerId cleared.");
            Assert.DoesNotContain(itemId, state.GamePlayers["p2"].OwnedClothingItemIds,
                "Item must be removed from the unclaimer's OwnedClothingItemIds.");
        }

        [TestMethod]
        public async Task OutfitBuilding_UnclaimPoolItem_ByWrongPlayer_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p3",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));
            Assert.AreEqual("p1", state.ClothingPool[itemId].ClaimedByPlayerId);

            // p2 tries to unclaim p1's item – must be rejected.
            _engine.ProcessCommand(context, new UnclaimPoolItemCommand("p2", itemId));

            Assert.AreEqual("p1", state.ClothingPool[itemId].ClaimedByPlayerId,
                "Only the claimant may unclaim an item.");
        }

        [TestMethod]
        public async Task OutfitBuilding_UnclaimAndReclaim_AllowsNewClaimer()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p3",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // p1 claims, then unclaims, then p2 claims.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));
            _engine.ProcessCommand(context, new UnclaimPoolItemCommand("p1", itemId));
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));

            Assert.AreEqual("p2", state.ClothingPool[itemId].ClaimedByPlayerId,
                "After an unclaim the item must be available for a new claimer.");
            Assert.Contains(itemId, state.GamePlayers["p2"].OwnedClothingItemIds);
            Assert.DoesNotContain(itemId, state.GamePlayers["p1"].OwnedClothingItemIds,
                "Previous claimer must not retain ownership after unclaiming.");
        }

        // ── Outfit building – submit validation ───────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_UnownedItem_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            // p1 does NOT own this item (never claimed it).

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId }));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit,
                "SubmitOutfit must be rejected when the player does not own the item.");
        }

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_WrongClothingTypeSlot_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",   // player owns it (self-drawn)
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // Submitting the hat item under the "top" slot – type mismatch.
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Top] = hatId }));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit,
                "SubmitOutfit must be rejected when the item's clothing type does not match the slot.");
        }

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_ValidOwnedItems_IsAccepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",   // drawn by another player
                SvgContent = "<svg/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit,
                "A valid outfit with owned items must be accepted.");
            Assert.AreEqual(hatId, state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType[ClothingType.Hat]);
        }

        // ── Outfit building – auto-fill ───────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillsIncompleteOutfit()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit);

            // Expire the timer.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit,
                "Auto-fill must produce an outfit when the timer expires.");
            Assert.IsTrue(state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType.ContainsKey(ClothingType.Hat),
                "Auto-filled outfit must include the available hat slot.");
        }

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillPrefersNonSelfDrawnItems()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            var selfDrawnHat = Guid.NewGuid();
            state.ClothingPool[selfDrawnHat] = new()
            {
                Id = selfDrawnHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",   // self-drawn
                SvgContent = "<svg self/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(selfDrawnHat);

            var claimedHat = Guid.NewGuid();
            state.ClothingPool[claimedHat] = new()
            {
                Id = claimedHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",   // drawn by p2, claimed by p1
                SvgContent = "<svg other/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(claimedHat);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var chosen = state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType[ClothingType.Hat];
            Assert.AreEqual(claimedHat, chosen,
                "Auto-fill must prefer a non-self-drawn (claimed) item over a self-drawn one.");
        }

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillFallsBackToSelfDrawnWhenNothingElse()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var selfDrawnHat = Guid.NewGuid();
            state.ClothingPool[selfDrawnHat] = new()
            {
                Id = selfDrawnHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(selfDrawnHat);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit,
                "Auto-fill must fall back to the self-drawn item when no other options exist.");
            Assert.AreEqual(selfDrawnHat,
                state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType[ClothingType.Hat]);
        }

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AlreadySubmittedOutfit_IsNotOverwritten()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // Player submits before the timer runs out.
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId }));

            var originalOutfit = state.GamePlayers["p1"].SubmittedOutfit;
            Assert.IsNotNull(originalOutfit);

            // Simulate a second tick (e.g., a late server tick after the deadline).
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.AreSame(originalOutfit, state.GamePlayers["p1"].SubmittedOutfit,
                "Auto-fill must not overwrite an outfit already submitted by the player.");
        }

        // ── StartAsync player snapshot (regression: GamePlayers was never populated) ──

        [TestMethod]
        public async Task StartAsync_PopulatesGamePlayersFromRegisteredPlayers()
        {
            // Arrange: create state and register a player.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;

            var player = new User("Alice", "alice1");
            state.RegisterPlayer(player);

            // Act: start the game (default config → ThemeSource.Random → DrawingRoundState).
            await _engine.StartAsync(_host, state);

            // Assert: registered player is now in GamePlayers with correct identity.
            Assert.IsTrue(state.GamePlayers.ContainsKey("alice1"),
                "StartAsync must snapshot registered players into GamePlayers.");
            Assert.AreEqual("Alice", state.GamePlayers["alice1"].DisplayName);
            Assert.AreEqual("alice1", state.GamePlayers["alice1"].PlayerId);
        }

        [TestMethod]
        public async Task StartAsync_HostIsNotAddedToGamePlayers()
        {
            // Arrange
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;

            // Register one non-host player.
            var player = new User("Alice", "alice1");
            state.RegisterPlayer(player);

            // Act
            await _engine.StartAsync(_host, state);

            // Assert: the host must not appear in GamePlayers.
            Assert.IsFalse(state.GamePlayers.ContainsKey(_host.Id),
                "The host must not be added to GamePlayers.");
            Assert.HasCount(1, state.GamePlayers,
                "Only the registered non-host player should be in GamePlayers.");
        }

        [TestMethod]
        public async Task SubmitDrawing_WithRegisteredPlayer_AddsItemToPool()
        {
            // Arrange: start with a single clothing type and one registered player.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];

            var player = new User("Alice", "alice1");
            state.RegisterPlayer(player);

            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Act: the registered player submits a drawing.
            var result = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("alice1", ClothingType.Hat, "<svg/>"));

            // Assert: submission succeeds and the item is added to the pool.
            Assert.IsTrue((bool)result.IsSuccess);
            Assert.HasCount(1, state.ClothingPool,
                "One drawing should be in the pool after submission.");
            var item = state.ClothingPool.Values.Single();
            Assert.AreEqual("alice1", item.CreatorPlayerId);
            Assert.AreEqual(ClothingType.Hat, item.ClothingTypeId);
            Assert.IsTrue(item.IsInPool);
        }

        [TestMethod]
        public async Task SubmitDrawing_MaxItemsEnforced_AfterRegisteredPlayerReachesLimit()
        {
            // Arrange: limit is 1 item per round.
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat", MaxItemsPerRound = 1 },
            ];

            var player = new User("Alice", "alice1");
            state.RegisterPlayer(player);
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // First submission should succeed.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("alice1", ClothingType.Hat, "<svg/>"));
            Assert.HasCount(1, state.ClothingPool);

            // Second submission must be rejected (limit = 1).
            _engine.ProcessCommand(context, new SubmitDrawingCommand("alice1", ClothingType.Hat, "<svg/>"));
            Assert.HasCount(1, state.ClothingPool,
                "A second submission beyond the per-round limit must not add another item.");
        }

        // ── Outfit customization: name-required ───────────────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithName_SetsNameAndMarksReady()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Use two players so one submitting does not advance the state.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new() { PlayerId = "p2" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context, new SubmitCustomizationCommand("p1", "My Cool Outfit"));

            Assert.IsTrue(state.GamePlayers["p1"].IsReady);
            Assert.AreEqual("My Cool Outfit", state.GamePlayers["p1"].SubmittedOutfit!.Customization.OutfitName);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithNullName_IsIgnored()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context, new SubmitCustomizationCommand("p1", null));

            Assert.IsFalse(state.GamePlayers["p1"].IsReady,
                "Player must not be marked ready when OutfitName is null.");
            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit!.Customization.OutfitName,
                "Outfit name must not be changed when submission is rejected.");
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithWhitespaceName_IsIgnored()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context, new SubmitCustomizationCommand("p1", "   "));

            Assert.IsFalse(state.GamePlayers["p1"].IsReady,
                "Player must not be marked ready when OutfitName is whitespace-only.");
        }

        // ── Outfit customization: sketch overlay ──────────────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithSketch_PersistsSketchContent()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Sketched Outfit", "<svg>sketch</svg>"));

            Assert.AreEqual("<svg>sketch</svg>",
                state.GamePlayers["p1"].SubmittedOutfit!.Customization.SketchSvgContent);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithFaceAndMannequin_PersistsFields()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Fashion Forward", null, null, FaceType.Devious, true));

            var customization = state.GamePlayers["p1"].SubmittedOutfit!.Customization;
            Assert.AreEqual(FaceType.Devious, customization.SelectedFace);
            Assert.IsTrue(customization.ShowMannequin);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithoutFaceAndMannequin_UsesDefaults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Plain Jane"));

            var customization = state.GamePlayers["p1"].SubmittedOutfit!.Customization;
            Assert.AreEqual(FaceType.Default, customization.SelectedFace);
            Assert.IsFalse(customization.ShowMannequin);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitWithoutSketch_SketchIsNull()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Plain Outfit"));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit!.Customization.SketchSvgContent,
                "Sketch should be null when no SVG is provided.");
        }

        // ── Outfit customization: SketchingRequired enforcement ───────────────

        [TestMethod]
        public async Task OutfitCustomizationState_SketchingRequired_WithSketch_Accepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.SketchingRequired = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Use two players so one submitting does not advance the state.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new() { PlayerId = "p2" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "My Outfit", "<svg>required sketch</svg>"));

            Assert.IsTrue(state.GamePlayers["p1"].IsReady,
                "Submission with a sketch must be accepted when SketchingRequired is enabled.");
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SketchingRequired_WithoutSketch_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.SketchingRequired = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "My Outfit"));

            Assert.IsFalse(state.GamePlayers["p1"].IsReady,
                "Submission without a sketch must be rejected when SketchingRequired is enabled.");
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SketchingNotRequired_WithoutSketch_Accepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.SketchingRequired = false;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Use two players so one submitting does not advance the state.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new() { PlayerId = "p2" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "No Sketch Outfit"));

            Assert.IsTrue(state.GamePlayers["p1"].IsReady,
                "Submission without a sketch must be accepted when SketchingRequired is false.");
        }

        // ── Outfit customization: all-ready early advance ─────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_AllPlayersSubmit_AdvancesEarly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new() { PlayerId = "p2" },
            };

            context.Config.NumOutfitRounds = 2;
            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Outfit One"));
            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState,
                "Should still be in customization after only one player submits.");

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p2", "Outfit Two"));
            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState,
                "Should advance once all players have submitted customization.");
        }

        // ── Outfit customization: submission persistence ───────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_SubmittedData_PersistedOnOutfitSubmission()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = itemId },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Hat Outfit", "<svg>overlay</svg>"));

            var submission = state.GamePlayers["p1"].SubmittedOutfit!;
            Assert.AreEqual("Hat Outfit", submission.Customization.OutfitName);
            Assert.AreEqual("<svg>overlay</svg>", submission.Customization.SketchSvgContent);
            Assert.IsTrue(submission.SelectedItemsByType.ContainsKey(ClothingType.Hat),
                "Original selected items must be preserved after customization.");
            Assert.AreEqual(itemId, submission.SelectedItemsByType[ClothingType.Hat]);
        }

        // ── Pool 2 Reveal state ───────────────────────────────────────────────

        [TestMethod]
        public async Task PoolRevealState_OnEnter_SetsPool2RevealPhase()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new PoolRevealState(outfitRound: 2));

            Assert.AreEqual(GamePhase.PoolReveal, state.Phase);
            Assert.IsTrue(state.PhaseDeadlineUtc.HasValue,
                "PoolRevealState must set a deadline on entry.");
        }

        [TestMethod]
        public async Task PoolRevealState_TimerExpiry_AdvancesToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new PoolRevealState(outfitRound: 2));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task PoolRevealState_Round2_AllPlayersReady_AdvancesEarlyToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            context.Fsm.TransitionTo(context, new PoolRevealState(outfitRound: 2));
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState,
                "All-ready should advance early from PoolReveal to OutfitBuilding.");
        }

        [TestMethod]
        public async Task PoolRevealState_ClaimPoolItem_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p2",
                SvgContent = "<svg/>", IsInPool = true,
            };
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            context.Fsm.TransitionTo(context, new PoolRevealState(outfitRound: 2));
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", hatId));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState,
                "Pool reveal is view-only — a ClaimPoolItemCommand must not advance the state.");
            Assert.IsNull(state.ClothingPool[hatId].ClaimedByPlayerId,
                "Items must not be claimable during Pool2Reveal.");
        }

        [TestMethod]
        public async Task PoolRevealState_OnEnter_ResetsPoolFromOutfit1Picks()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var usedHat = Guid.NewGuid();
            var freeHat = Guid.NewGuid();
            state.ClothingPool[usedHat] = new()
            {
                Id = usedHat, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p2",
                SvgContent = "<svg/>", IsInPool = true,
            };
            state.ClothingPool[freeHat] = new()
            {
                Id = freeHat, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p3",
                SvgContent = "<svg/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = usedHat },
                },
            };

            context.Fsm.TransitionTo(context, new PoolRevealState(outfitRound: 2));

            Assert.IsFalse(state.ClothingPool[usedHat].IsInPool,
                "Item selected in Outfit 1 must be removed from the Outfit 2 pool during Pool2Reveal.");
            Assert.IsTrue(state.ClothingPool[freeHat].IsInPool,
                "Item not selected in any Outfit 1 must remain in the Outfit 2 pool.");
        }

        // ── Outfit 2: pool reset on entry ─────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_OnEnter_RemovesOutfit1PicksFromPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Two items in the pool: one selected in Outfit 1, one not.
            var usedHat = Guid.NewGuid();
            var unusedHat = Guid.NewGuid();
            state.ClothingPool[usedHat] = new()
            {
                Id = usedHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.ClothingPool[unusedHat] = new()
            {
                Id = unusedHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p3",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = usedHat },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            Assert.IsFalse(state.ClothingPool[usedHat].IsInPool,
                "Item selected in Outfit 1 must be removed from the Outfit 2 pool.");
            Assert.IsTrue(state.ClothingPool[unusedHat].IsInPool,
                "Item not selected in any Outfit 1 must remain in the Outfit 2 pool.");
        }

        [TestMethod]
        public async Task OutfitBuilding_OnEnter_ClearsAllClaims()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // A previously claimed item.
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",  // was claimed in Outfit 1
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new Dictionary<ClothingType, Guid>(), // different item selected
                },
                OwnedClothingItemIds = [itemId],
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            Assert.IsNull(state.ClothingPool[itemId].ClaimedByPlayerId,
                "All claims must be cleared when Outfit 2 building begins.");
        }

        [TestMethod]
        public async Task OutfitBuilding_OnEnter_SelfDrawnItemsInPool_RemainsOwned()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // p1 drew a hat that is NOT in their Outfit 1, so it remains in the pool.
            var selfHat = Guid.NewGuid();
            state.ClothingPool[selfHat] = new()
            {
                Id = selfHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new Dictionary<ClothingType, Guid>(), // selfHat not selected
                },
                OwnedClothingItemIds = [selfHat],
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            Assert.Contains(selfHat, state.GamePlayers["p1"].OwnedClothingItemIds,
                "A self-drawn item that is still in the pool must remain in the player's owned set.");
        }

        [TestMethod]
        public async Task OutfitBuilding_OnEnter_Outfit1SelfDrawnItemUsed_NotInOwned()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // p1 drew a hat AND used it in Outfit 1; it must be excluded from Outfit 2 pool.
            var selfHat = Guid.NewGuid();
            state.ClothingPool[selfHat] = new()
            {
                Id = selfHat,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = selfHat },
                },
                OwnedClothingItemIds = [selfHat],
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            Assert.DoesNotContain(selfHat, state.GamePlayers["p1"].OwnedClothingItemIds,
                "A self-drawn item used in Outfit 1 must not be in the player's Outfit 2 owned set " +
                "when CanReuseOutfit1Items is false.");
        }

        // ── Outfit 2: CanReuseOutfit1Items ────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_CanReuseOutfit1Items_True_AddsBackOutfit1Picks()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId },
                },
                OwnedClothingItemIds = [hatId],
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            // The hat was in Outfit 1 (now IsInPool=false), but CanReuseOutfit1Items allows it back.
            Assert.Contains(hatId, state.GamePlayers["p1"].OwnedClothingItemIds,
                "When CanReuseOutfit1Items is true the player's own Outfit 1 picks must remain owned.");
        }

        [TestMethod]
        public async Task OutfitBuilding_CanReuseOutfit1Items_False_DoesNotAddBackOutfit1Picks()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.CanReuseOutfit1Items = false;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId },
                },
                OwnedClothingItemIds = [hatId],
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            Assert.DoesNotContain(hatId, state.GamePlayers["p1"].OwnedClothingItemIds,
                "When CanReuseOutfit1Items is false the player's Outfit 1 picks must not be owned for Outfit 2.");
        }

        // ── Outfit 2: claim / unclaim ─────────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_Round2_ClaimPoolItem_Success_AddsToOwnedList()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", hatId));

            Assert.AreEqual("p1", state.ClothingPool[hatId].ClaimedByPlayerId);
            Assert.Contains(hatId, state.GamePlayers["p1"].OwnedClothingItemIds);
        }

        [TestMethod]
        public async Task OutfitBuilding_ClaimPoolItem_ItemRemovedByReset_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            // p2 used hatId in their Outfit 1.
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new()
                {
                    PlayerId = "p2",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            // hatId was in Outfit 1 → IsInPool = false → claim must be rejected.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", hatId));

            Assert.IsNull(state.ClothingPool[hatId].ClaimedByPlayerId,
                "An item removed from the Outfit 2 pool must not be claimable.");
            Assert.DoesNotContain(hatId, state.GamePlayers["p1"].OwnedClothingItemIds);
        }

        // ── Outfit 2: submit validation ───────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_ValidAndDistinct_IsAccepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var outfit1Hat = Guid.NewGuid();
            var outfit2Hat = Guid.NewGuid();

            state.ClothingPool[outfit1Hat] = new()
            {
                Id = outfit1Hat, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p2", SvgContent = "<svg/>", IsInPool = false,
            };
            // outfit2Hat is self-drawn by p1 → after pool reset it will be auto-owned.
            state.ClothingPool[outfit2Hat] = new()
            {
                Id = outfit2Hat, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p1", SvgContent = "<svg/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = outfit1Hat },
                },
            };

            // Pool reset: outfit1Hat excluded (in Outfit 1 picks); outfit2Hat stays (self-drawn by p1).
            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = outfit2Hat }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "A valid, distinct Outfit 2 submission must be accepted.");
            Assert.AreEqual(outfit2Hat, state.GamePlayers["p1"].SubmittedOutfit2!.SelectedItemsByType[ClothingType.Hat]);
        }

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_ViolatesDistinctness_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
                new() { Id = ClothingType.Top, DisplayName = "Top" },
                new() { Id = ClothingType.Shoes, DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            // Allow reuse so the player still owns their Outfit 1 picks in Outfit 2.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            foreach (var (id, type) in new[] { (hatId, ClothingType.Hat), (topId, ClothingType.Top), (shoesId, ClothingType.Shoes) })
            {
                state.ClothingPool[id] = new()
                {
                    Id = id, ClothingTypeId = type, CreatorPlayerId = "p2",
                    SvgContent = "<svg/>", IsInPool = true,
                };
            }

            // p1's Outfit 1 uses hatId, topId, shoesId.
            // With CanReuseOutfit1Items=true the pool reset re-adds them to p1's owned set.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId }));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "An Outfit 2 that shares 3+ items with any Outfit 1 must be rejected.");
        }

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_MatchesOtherPlayersOutfit1_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
                new() { Id = ClothingType.Top, DisplayName = "Top" },
                new() { Id = ClothingType.Shoes, DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            foreach (var (id, type) in new[] { (hatId, ClothingType.Hat), (topId, ClothingType.Top), (shoesId, ClothingType.Shoes) })
            {
                state.ClothingPool[id] = new()
                {
                    Id = id, ClothingTypeId = type, CreatorPlayerId = "p3",
                    SvgContent = "<svg/>", IsInPool = true,
                };
            }

            // p1's Outfit 1 is empty; p2's Outfit 1 uses hatId, topId, shoesId.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new()
                {
                    PlayerId = "p2",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
                },
            };

            // Pool reset removes hatId/topId/shoesId (in p2's Outfit 1) from pool.
            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            // Manually grant p1 access to those items to isolate the distinctness-check logic.
            // (In game play this could happen if an item appears in multiple Outfit 1s via
            // CanReuseOutfit1Items, but here we directly test that the cross-player check fires.)
            state.GamePlayers["p1"].OwnedClothingItemIds.AddRange([hatId, topId, shoesId]);

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId }));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "An Outfit 2 must be rejected when it is too similar to another player's Outfit 1.");
        }

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_DistinctnessDisabled_AllowsSimilarOutfit()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
                new() { Id = ClothingType.Top, DisplayName = "Top" },
                new() { Id = ClothingType.Shoes, DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 0; // disabled
            // Allow reuse so the player still owns their Outfit 1 picks in Outfit 2.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            foreach (var (id, type) in new[] { (hatId, ClothingType.Hat), (topId, ClothingType.Top), (shoesId, ClothingType.Shoes) })
            {
                state.ClothingPool[id] = new()
                {
                    Id = id, ClothingTypeId = type, CreatorPlayerId = "p2",
                    SvgContent = "<svg/>", IsInPool = true,
                };
            }

            // p1's Outfit 1 uses the same items; CanReuseOutfit1Items re-adds them to owned after reset.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "When distinctness is disabled (threshold=0) identical outfits must be accepted.");
        }

        [TestMethod]
        public async Task OutfitBuilding_SubmitOutfit_BelowThreshold_IsAccepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
                new() { Id = ClothingType.Top, DisplayName = "Top" },
                new() { Id = ClothingType.Shoes, DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            // CanReuseOutfit1Items so hatId/topId/outfit1Shoes are re-owned after pool reset.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var outfit1Shoes = Guid.NewGuid();
            // outfit2Shoes is self-drawn by p1 so it will be auto-owned after reset.
            var outfit2Shoes = Guid.NewGuid();

            foreach (var (id, type) in new[]
            {
                (hatId, ClothingType.Hat), (topId, ClothingType.Top), (outfit1Shoes, ClothingType.Shoes),
            })
            {
                state.ClothingPool[id] = new()
                {
                    Id = id, ClothingTypeId = type, CreatorPlayerId = "p2",
                    SvgContent = "<svg/>", IsInPool = true,
                };
            }
            state.ClothingPool[outfit2Shoes] = new()
            {
                Id = outfit2Shoes, ClothingTypeId = ClothingType.Shoes, CreatorPlayerId = "p1",
                SvgContent = "<svg/>", IsInPool = true,
            };

            // Outfit 1 uses hat, top, outfit1Shoes.
            // After reset with CanReuseOutfit1Items: hat, top, outfit1Shoes re-owned.
            // outfit2Shoes: self-drawn by p1 and not in Outfit 1 → auto-owned.
            // Outfit 2 submits hat + top (2 shared) + outfit2Shoes → below threshold of 3 → accepted.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = outfit1Shoes },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = outfit2Shoes }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "Outfit 2 sharing fewer than the threshold items with Outfit 1 must be accepted.");
        }

        // ── Outfit 2: early advance ───────────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_AllPlayersSubmit_AdvancesToVoting()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 0; // disable to simplify
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var hatA = Guid.NewGuid();
            var hatB = Guid.NewGuid();
            state.ClothingPool[hatA] = new()
            {
                Id = hatA, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p2", SvgContent = "<svg/>", IsInPool = true,
            };
            state.ClothingPool[hatB] = new()
            {
                Id = hatB, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p1", SvgContent = "<svg/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() },
                OwnedClothingItemIds = [hatB],
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new() { PlayerId = "p2", SelectedItemsByType = new() },
                OwnedClothingItemIds = [hatA],
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatB }));
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState,
                "Should not advance until all players have submitted Outfit 2.");

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p2",
                new Dictionary<ClothingType, Guid> { [ClothingType.Hat] = hatA }));
            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState,
                "Should advance to Outfit 2 customization once all players submit Outfit 2.");
        }

        // ── Outfit 2: timer expiry auto-fill ──────────────────────────────────

        // ── Outfit 2 Customization state ──────────────────────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_OnEnter_SetsOutfit2CustomizationPhase()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new OutfitCustomizationState(outfitRound: 2));

            Assert.AreEqual(GamePhase.OutfitCustomization, state.Phase);
            Assert.IsTrue(state.PhaseDeadlineUtc.HasValue,
                "OutfitCustomizationState must set a deadline on entry.");
        }

        [TestMethod]
        public async Task OutfitCustomizationState_TimerExpiry_TransitionsToVotingRoundSetup()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new OutfitCustomizationState(outfitRound: 2));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // VotingRoundSetupState chains immediately to VotingMatchupState.
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_SubmitCustomization_StoresInOutfit2()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit2 = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState(outfitRound: 2));
            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Second Look"));

            Assert.AreEqual("Second Look",
                state.GamePlayers["p1"].SubmittedOutfit2!.Customization.OutfitName,
                "Customization must be stored in SubmittedOutfit2, not SubmittedOutfit.");
        }

        [TestMethod]
        public async Task OutfitCustomizationState_AllPlayersSubmit_AdvancesEarlyToVoting()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit2 = new() { PlayerId = "p1" },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit2 = new() { PlayerId = "p2" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState(outfitRound: 2));

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Look One"));
            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState,
                "Should stay in Outfit2Customization until all players submit.");

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p2", "Look Two"));
            // VotingRoundSetupState chains immediately to VotingMatchupState.
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState,
                "Should advance to voting once all Outfit 2 customizations are submitted.");
        }

        [TestMethod]
        public async Task OutfitCustomizationState_NoOutfit2Submitted_RejectsCommand()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Player has Outfit 1 but no Outfit 2.
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1" },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState(outfitRound: 2));
            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Should Fail"));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit?.Customization.OutfitName,
                "SubmitCustomizationCommand must be rejected when SubmittedOutfit2 is null.");
            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState,
                "State must not advance if SubmittedOutfit2 is missing.");
        }

        // ── Outfit 2: timer expiry auto-fill ──────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillsIncompleteOutfit2()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 0; // disable to keep auto-fill simple
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Hat is self-drawn by p1 and not selected in p1's Outfit 1 →
            // after pool reset it stays in pool and is auto-owned by p1.
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p1", SvgContent = "<svg/>", IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() }, // empty Outfit 1
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit2);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "Auto-fill must produce an Outfit 2 when the timer expires.");
            Assert.IsTrue(state.GamePlayers["p1"].SubmittedOutfit2!.SelectedItemsByType.ContainsKey(ClothingType.Hat));
        }

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillPrefersNonConflictingItems()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = ClothingType.Hat, DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 1; // any shared item is a violation
            // Allow reuse so p1 owns both conflictHat and distinctHat after reset.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var conflictHat = Guid.NewGuid(); // also in p1's Outfit 1
            var distinctHat = Guid.NewGuid(); // not in any Outfit 1

            // conflictHat was used in p1's Outfit 1 → will be excluded from pool after reset
            // but CanReuseOutfit1Items will add it back to p1's owned set.
            // distinctHat is self-drawn by p1 and not in any Outfit 1 → stays in pool.
            state.ClothingPool[conflictHat] = new()
            {
                Id = conflictHat, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p2",
                SvgContent = "<svg conflict/>", IsInPool = true,
            };
            state.ClothingPool[distinctHat] = new()
            {
                Id = distinctHat, ClothingTypeId = ClothingType.Hat, CreatorPlayerId = "p1",
                SvgContent = "<svg distinct/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { [ClothingType.Hat] = conflictHat },
                },
            };

            // After pool reset:
            //   conflictHat: IsInPool=false (in p1's Outfit 1); re-added to p1's owned via CanReuseOutfit1Items.
            //   distinctHat: IsInPool=true (not in Outfit 1); auto-owned (self-drawn by p1).
            // Auto-fill prefers distinctHat because it doesn't appear in any Outfit 1.
            context.Fsm.TransitionTo(context, new OutfitBuildingState(outfitRound: 2));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var chosenHat = state.GamePlayers["p1"].SubmittedOutfit2!.SelectedItemsByType[ClothingType.Hat];
            Assert.AreEqual(distinctHat, chosenHat,
                "Auto-fill must prefer an item that does not appear in any Outfit 1 over one that does.");
        }

        // ── OutfitDistinctnessEvaluator unit tests ────────────────────────────

        [TestMethod]
        public void OutfitDistinctnessEvaluator_CountSharedItems_NoOverlap_ReturnsZero()
        {
            var hat1 = Guid.NewGuid();
            var hat2 = Guid.NewGuid();

            var outfit1 = new OutfitSubmission { SelectedItemsByType = new() { [ClothingType.Hat] = hat1 } };
            var outfit2 = new OutfitSubmission { SelectedItemsByType = new() { [ClothingType.Hat] = hat2 } };

            Assert.AreEqual(0, OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2));
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_CountSharedItems_FullOverlap_ReturnsCount()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();

            var outfit1 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId },
            };

            Assert.AreEqual(2, OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2));
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_CountSharedItems_SameSlotDifferentItem_IsZero()
        {
            var hatA = Guid.NewGuid();
            var hatB = Guid.NewGuid();

            var outfit1 = new OutfitSubmission { SelectedItemsByType = new() { [ClothingType.Hat] = hatA } };
            var outfit2 = new OutfitSubmission { SelectedItemsByType = new() { [ClothingType.Hat] = hatB } };

            Assert.AreEqual(0, OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2),
                "Items in the same slot but with different IDs must not count as shared.");
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_ViolatesDistinctnessRule_Disabled_ReturnsFalse()
        {
            var hatId = Guid.NewGuid();
            var outfit1 = new OutfitSubmission { SelectedItemsByType = new() { [ClothingType.Hat] = hatId } };
            var outfit2 = new OutfitSubmission { SelectedItemsByType = new() { [ClothingType.Hat] = hatId } };

            Assert.IsFalse(
                OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(outfit2, [outfit1], threshold: 0),
                "ViolatesDistinctnessRule must always return false when threshold is 0 (disabled).");
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_ViolatesDistinctnessRule_AtThreshold_ReturnsTrue()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            var outfit1 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
            };

            Assert.IsTrue(
                OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(outfit2, [outfit1], threshold: 3),
                "Sharing exactly the threshold number of items must be treated as a violation.");
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_ViolatesDistinctnessRule_BelowThreshold_ReturnsFalse()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var newShoes = Guid.NewGuid();

            var outfit1 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = Guid.NewGuid() },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = newShoes },
            };

            Assert.IsFalse(
                OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(outfit2, [outfit1], threshold: 3),
                "Sharing fewer items than the threshold must not be a violation.");
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_ViolatesDistinctnessRule_AgainstMultipleOutfit1s_DetectsAny()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            // Outfit2 only shares 3 items with outfit1B (not outfit1A).
            var outfit1A = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = Guid.NewGuid(), [ClothingType.Top] = Guid.NewGuid() },
            };
            var outfit1B = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
            };

            Assert.IsTrue(
                OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(outfit2, [outfit1A, outfit1B], threshold: 3),
                "Violating distinctness against any single Outfit 1 must be detected even when other Outfit 1s are fine.");
        }

        // ── Voting eligibility enforcement ────────────────────────────────────

        [TestMethod]
        public async Task VotingMatchupState_Participant_CastVote_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Set up players with submitted outfits so they become entrants.
            var outfit1 = new OutfitSubmission { PlayerId = "pA" };
            var outfit2 = new OutfitSubmission { PlayerId = "pB" };
            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = outfit1 };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = outfit2 };
            state.GamePlayers["pC"] = new() { PlayerId = "pC", SubmittedOutfit = new() { PlayerId = "pC" } };

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            // Find a matchup that contains pA's outfit.
            var currentRound = state.VotingRounds[state.CurrentVotingRoundIndex];
            var pAMatchup = currentRound.Matchups.First(m =>
                m.EntrantAId.PlayerId == "pA" ||
                m.EntrantBId.PlayerId == "pA");

            // pA tries to vote on their own matchup — must be rejected.
            int votesBefore = state.Votes.Count;
            _engine.ProcessCommand(context,
                new CastVoteCommand("pA", pAMatchup.Id, "creativity", pAMatchup.EntrantBId));
            Assert.HasCount(votesBefore, state.Votes,
                "A participant's vote on their own matchup must be ignored.");
        }

        [TestMethod]
        public async Task VotingMatchupState_NonParticipant_CastVote_IsAccepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };
            state.GamePlayers["pC"] = new() { PlayerId = "pC", SubmittedOutfit = new() { PlayerId = "pC" } };

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            var currentRound = state.VotingRounds[state.CurrentVotingRoundIndex];
            var firstMatchup = currentRound.Matchups[0];

            // Find a player who is NOT a creator of either entrant in the first matchup.
            var entAPlayer = firstMatchup.EntrantAId.PlayerId;
            var entBPlayer = firstMatchup.EntrantBId.PlayerId;
            string outsider = new[] { "pA", "pB", "pC" }
                .First(id => id != entAPlayer && id != entBPlayer);

            int votesBefore = state.Votes.Count;
            _engine.ProcessCommand(context,
                new CastVoteCommand(outsider, firstMatchup.Id, "creativity", firstMatchup.EntrantAId));
            Assert.HasCount(votesBefore + 1, state.Votes,
                "A non-participant's vote must be recorded.");
        }

        // ── Swiss pairing: entrant registration ───────────────────────────────

        [TestMethod]
        public async Task VotingRoundSetupState_OnlyPlayersWithOutfits_AreEntrants()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // pA has submitted an outfit; pB has not.
            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB" }; // no outfit

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());

            var round = state.VotingRounds[0];
            // pB must not appear in any matchup.
            var allPlayerIds = round.Matchups
                .SelectMany(m => new[] { m.EntrantAId, m.EntrantBId })
                .Select(e => e.PlayerId)
                .ToList();
            CollectionAssert.DoesNotContain(allPlayerIds, "pB",
                "Players without submitted outfits must not be included as tournament entrants.");
        }

        [TestMethod]
        public async Task VotingRoundSetupState_PlayerWithOutfit2Only_IsEntrant()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // pA has only Outfit 2 submitted (e.g. in a multi-outfit scenario).
            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit2 = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit2 = new() { PlayerId = "pB" } };

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());

            var round = state.VotingRounds[0];
            var participantPlayerIds = round.Matchups
                .SelectMany(m => new[] { m.EntrantAId, m.EntrantBId })
                .Select(e => e.PlayerId)
                .ToHashSet();
            Assert.IsTrue(participantPlayerIds.Contains("pA") || participantPlayerIds.Contains("pB"),
                "Players with a submitted Outfit 2 must be included as entrants.");
        }

        // ── OutfitDistinctnessResolutionState: HandleCommand ──────────────────

        [TestMethod]
        public async Task OutfitDistinctnessResolutionState_OnEnter_SetsPhase()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new OutfitDistinctnessResolutionState());

            Assert.IsInstanceOfType<OutfitDistinctnessResolutionState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitDistinctnessResolution, state.Phase);
        }

        [TestMethod]
        public async Task OutfitDistinctnessResolutionState_ResolveDistinctnessCommand_MarksPlayerReady()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes = [new() { Id = ClothingType.Hat, DisplayName = "Hat" }];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Give p1 a submitted outfit and a replacement item in the pool.
            var replacementId = Guid.NewGuid();
            state.ClothingPool[replacementId] = new()
            {
                Id = replacementId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() { [ClothingType.Hat] = Guid.NewGuid() } },
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new() { PlayerId = "p2", SelectedItemsByType = new() { [ClothingType.Hat] = Guid.NewGuid() } },
            };

            context.Fsm.TransitionTo(context, new OutfitDistinctnessResolutionState());

            // p1 resolves their conflict by picking the replacement hat.
            _engine.ProcessCommand(context, new ResolveDistinctnessCommand("p1", replacementId));

            Assert.IsTrue(state.GamePlayers["p1"].IsReady,
                "Player must be marked ready after successfully resolving a distinctness conflict.");
            Assert.AreEqual(replacementId, state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType[ClothingType.Hat],
                "The player's outfit must use the replacement item after resolving.");
        }

        [TestMethod]
        public async Task OutfitDistinctnessResolutionState_SinglePlayer_Resolves_AdvancesToPool2Reveal()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes = [new() { Id = ClothingType.Hat, DisplayName = "Hat" }];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var replacementId = Guid.NewGuid();
            state.ClothingPool[replacementId] = new()
            {
                Id = replacementId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() { [ClothingType.Hat] = Guid.NewGuid() } },
            };

            context.Fsm.TransitionTo(context, new OutfitDistinctnessResolutionState());

            // p1 is the only player — resolving should advance to next phase.
            _engine.ProcessCommand(context, new ResolveDistinctnessCommand("p1", replacementId));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState,
                "Resolving all conflicts must advance to Pool 2 Reveal.");
        }

        [TestMethod]
        public async Task OutfitDistinctnessResolutionState_UnknownPlayer_IsIgnored()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new OutfitDistinctnessResolutionState());

            // An unknown player sends the command — must remain in the same state.
            _engine.ProcessCommand(context, new ResolveDistinctnessCommand("ghost", Guid.NewGuid()));

            Assert.IsInstanceOfType<OutfitDistinctnessResolutionState>(context.Fsm.CurrentState,
                "A command from an unknown player must not change state.");
        }

        [TestMethod]
        public async Task OutfitDistinctnessResolutionState_InvalidReplacementItem_IsIgnored()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() },
            };

            context.Fsm.TransitionTo(context, new OutfitDistinctnessResolutionState());

            // A non-existent replacement item must be rejected.
            _engine.ProcessCommand(context, new ResolveDistinctnessCommand("p1", Guid.NewGuid()));

            Assert.IsFalse(state.GamePlayers["p1"].IsReady,
                "Using a non-existent replacement item must not mark the player ready.");
        }

        [TestMethod]
        public async Task OutfitDistinctnessResolutionState_PauseGameCommand_TransitionsToPaused()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            context.Fsm.TransitionTo(context, new OutfitDistinctnessResolutionState());
            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState,
                "PauseGameCommand from host must transition to PausedState.");
        }

        // ── VotingRoundResultsState: round leader bonus ───────────────────────

        [TestMethod]
        public async Task VotingRoundResultsState_OnEnter_AwardsRoundLeaderBonus()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // Config: single weight-1 criterion, +3 round leader bonus.
            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.RoundLeaderBonusPoints = 3;

            // Set up players.
            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            // Build a round with one matchup between pA:1 and pB:1.
            var matchupId = Guid.NewGuid();
            var round = new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            };
            state.VotingRounds.Add(round);
            state.CurrentVotingRoundIndex = 0;

            // pA wins by 2-0 votes.
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };

            // Entering VotingRoundResultsState should award the round leader bonus to pA.
            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            Assert.AreEqual(3, state.GamePlayers["pA"].BonusPoints,
                "The round leader must receive +3 bonus points on VotingRoundResultsState entry.");
            Assert.AreEqual(0, state.GamePlayers["pB"].BonusPoints,
                "The non-leader must not receive a round leader bonus.");
        }

        [TestMethod]
        public async Task VotingRoundResultsState_OnEnter_AwardsBonusToBothWhenTied()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.RoundLeaderBonusPoints = 3;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            var matchupId = Guid.NewGuid();
            state.VotingRounds.Add(new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            });
            state.CurrentVotingRoundIndex = 0;

            // One vote each — tied round.
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pB", 1) };

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            Assert.AreEqual(3, state.GamePlayers["pA"].BonusPoints,
                "Both tied leaders must receive the round leader bonus.");
            Assert.AreEqual(3, state.GamePlayers["pB"].BonusPoints,
                "Both tied leaders must receive the round leader bonus.");
        }

        [TestMethod]
        public async Task VotingRoundResultsState_OnEnter_ZeroBonusConfig_NoBonusAwarded()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.RoundLeaderBonusPoints = 0;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            var matchupId = Guid.NewGuid();
            state.VotingRounds.Add(new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            });
            state.CurrentVotingRoundIndex = 0;

            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            Assert.AreEqual(0, state.GamePlayers["pA"].BonusPoints,
                "When RoundLeaderBonusPoints is 0 no bonus must be awarded.");
        }

        // ── FinalResultsState: tournament winner bonus and leaderboard ─────────

        [TestMethod]
        public async Task FinalResultsState_OnEnter_AwardsTournamentWinnerBonus()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.TournamentWinnerBonusPoints = 10;
            state.Config.VotingRounds = 1;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            // pA wins the single round.
            var matchupId = Guid.NewGuid();
            state.VotingRounds.Add(new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            });
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };

            // FinalResultsState chains immediately to FinalResultsDisplayState.
            context.Fsm.TransitionTo(context, new FinalResultsState());

            Assert.AreEqual(10, state.GamePlayers["pA"].BonusPoints,
                "The tournament winner must receive the +10 winner bonus.");
            Assert.AreEqual(0, state.GamePlayers["pB"].BonusPoints,
                "The non-winner must not receive the tournament winner bonus.");
        }

        [TestMethod]
        public async Task FinalResultsState_OnEnter_BuildsLeaderboard()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.TournamentWinnerBonusPoints = 0;
            state.Config.VotingRounds = 1;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            var matchupId = Guid.NewGuid();
            state.VotingRounds.Add(new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            });
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };

            Assert.IsEmpty(state.Leaderboard, "Leaderboard must be empty before FinalResultsState.");

            context.Fsm.TransitionTo(context, new FinalResultsState());

            Assert.IsNotEmpty(state.Leaderboard,
                "FinalResultsState must populate the leaderboard on entry.");
        }

        [TestMethod]
        public async Task FinalResultsState_OnEnter_ZeroBonusConfig_NoBonusAwarded()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.TournamentWinnerBonusPoints = 0;
            state.Config.VotingRounds = 1;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            var matchupId = Guid.NewGuid();
            state.VotingRounds.Add(new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            });
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };

            context.Fsm.TransitionTo(context, new FinalResultsState());

            Assert.AreEqual(0, state.GamePlayers["pA"].BonusPoints,
                "When TournamentWinnerBonusPoints is 0 no bonus must be awarded.");
        }

        [TestMethod]
        public async Task FinalResultsState_OnEnter_TiedWinners_BothReceiveBonus()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.Config.VotingCriteria = [new() { Id = "c1", Weight = 1.0 }];
            state.Config.TournamentWinnerBonusPoints = 10;
            state.Config.VotingRounds = 1;

            state.GamePlayers["pA"] = new() { PlayerId = "pA", SubmittedOutfit = new() { PlayerId = "pA" } };
            state.GamePlayers["pB"] = new() { PlayerId = "pB", SubmittedOutfit = new() { PlayerId = "pB" } };

            var matchupId = Guid.NewGuid();
            state.VotingRounds.Add(new VotingRound
            {
                RoundNumber = 1,
                Matchups = [new SwissMatchup(matchupId, new EntrantId("pA", 1), new EntrantId("pB", 1), 1)],
            });

            // Tie: one vote each.
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pA", 1) };
            state.Votes[Guid.NewGuid()] = new() { MatchupId = matchupId, CriterionId = "c1", ChosenEntrantId = new EntrantId("pB", 1) };

            context.Fsm.TransitionTo(context, new FinalResultsState());

            Assert.AreEqual(10, state.GamePlayers["pA"].BonusPoints,
                "Tied winners must both receive the tournament winner bonus.");
            Assert.AreEqual(10, state.GamePlayers["pB"].BonusPoints,
                "Tied winners must both receive the tournament winner bonus.");
        }

        // ── OutfitDistinctnessEvaluator: self-comparison ──────────────────────

        [TestMethod]
        public void OutfitDistinctnessEvaluator_SelfComparison_SameItems_CountsAsShared()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();

            // A player's own Outfit 1 and Outfit 2 share the same items.
            var outfit1 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId },
            };

            int shared = OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2);

            Assert.AreEqual(2, shared,
                "Self-comparison must detect shared items between the player's own Outfit 1 and Outfit 2.");
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_SelfComparison_ViolatesRule_WhenAtThreshold()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            var outfit1 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { [ClothingType.Hat] = hatId, [ClothingType.Top] = topId, [ClothingType.Shoes] = shoesId },
            };

            Assert.IsTrue(
                OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(outfit2, [outfit1], threshold: 3),
                "A player's own identical Outfit 2 must violate the distinctness rule at threshold=3.");
        }

        // ── Pool exhaustion / full-pool-claimed edge case ─────────────────────

        [TestMethod]
        public async Task OutfitBuilding_AllItemsClaimed_NewClaimOnClaimedItem_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            state.GamePlayers["p3"] = new() { PlayerId = "p3" };

            // Single item drawn by p3 (so both p1 and p2 can claim but only one wins).
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p3",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // p1 claims the only available item.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));
            Assert.Contains(itemId, state.GamePlayers["p1"].OwnedClothingItemIds);

            // p2 tries to claim the same (now exhausted) item — must be rejected gracefully.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));

            Assert.DoesNotContain(itemId, state.GamePlayers["p2"].OwnedClothingItemIds,
                "Claiming an already-claimed item must be rejected; pool is effectively exhausted for that slot.");
            Assert.AreEqual("p1", state.ClothingPool[itemId].ClaimedByPlayerId,
                "First claimer's ownership must remain intact after a rejected second claim.");
        }

        // ── Simultaneous claims: first-processed wins ─────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_SimultaneousClaims_FirstProcessedWins()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = ClothingType.Hat,
                CreatorPlayerId = "p3",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // Simulate both players claiming at the same "instant" by sending both commands
            // in immediate succession. The first command processed wins.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));

            // Only one player should own the item; the other's claim must be rejected.
            bool p1Owns = state.GamePlayers["p1"].OwnedClothingItemIds.Contains(itemId);
            bool p2Owns = state.GamePlayers["p2"].OwnedClothingItemIds.Contains(itemId);

            Assert.IsTrue(p1Owns ^ p2Owns,
                "Exactly one player must win a simultaneous claim on the same item.");
            Assert.AreEqual("p1", state.ClothingPool[itemId].ClaimedByPlayerId,
                "The first-processed claim (p1) must win when two commands arrive simultaneously.");
        }
    }
}
