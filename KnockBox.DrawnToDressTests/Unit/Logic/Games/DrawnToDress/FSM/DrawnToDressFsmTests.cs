using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Tests FSM creation, state entry, and nominal phase transitions for Drawn To Dress.
    /// </summary>
    [TestClass]
    public class DrawnToDressFsmTests
    {
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _host = new User("Host", "host1");

            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
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
        public async Task StartAsync_TransitionsFromLobbyToDrawingPhase()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)startResult.IsSuccess);
            // For the default random theme source the chain is:
            // ThemeSelectionState → DrawingRoundState
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.IsInstanceOfType<DrawingRoundState>(state.Context!.Fsm.CurrentState);
            Assert.IsNotNull(state.PhaseDeadlineUtc);
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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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
                new() { Id = "hat",  DisplayName = "Hat",  MaxItemsPerRound = 3 },
                new() { Id = "top",  DisplayName = "Top",  MaxItemsPerRound = 3 },
                new() { Id = "shoes", DisplayName = "Shoes", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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
                new() { Id = "hat",  DisplayName = "Hat",  MaxItemsPerRound = 3 },
                new() { Id = "top",  DisplayName = "Top",  MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = "top", DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = "top", DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
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
                new() { Id = "hat",    DisplayName = "Hat",    MaxItemsPerRound = 3 },
                new() { Id = "top",    DisplayName = "Top",    MaxItemsPerRound = 3 },
                new() { Id = "bottom", DisplayName = "Bottom", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = "top", DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            // Currently in hat round; submitting "top" should be ignored.
            _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "top", "<svg/>"));

            Assert.AreEqual(0, state.ClothingPool.Count, "Wrong-type submission must be discarded.");
        }

        [TestMethod]
        public async Task DrawingRoundState_MaxItemsPerType_RejectsExcessSubmissions()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 2 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            // Submit up to the max.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>1</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>2</svg>"));
            Assert.AreEqual(2, state.ClothingPool.Count);

            // Third submission should be rejected.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>3</svg>"));
            Assert.AreEqual(2, state.ClothingPool.Count, "Submission beyond MaxItemsPerRound must be discarded.");
        }

        [TestMethod]
        public async Task DrawingRoundState_PlayerDrawsNothing_TimerAdvancesRound()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Player draws nothing — pool stays empty.
            Assert.AreEqual(0, state.ClothingPool.Count);

            // Timer still advances the game → PoolReveal.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(0, state.ClothingPool.Count, "Pool must remain empty when nothing was drawn.");

            // Advance through pool reveal timer → OutfitBuilding.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(0, state.ClothingPool.Count, "Pool must remain empty when nothing was drawn.");
        }

        [TestMethod]
        public async Task DrawingRoundState_SubmitDrawing_StoredWithCreatorAttribution()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            _engine.ProcessCommand(context,
                new SubmitDrawingCommand("p1", "hat", "<svg>my hat</svg>"));

            Assert.AreEqual(1, state.ClothingPool.Count);
            var item = state.ClothingPool.Values.Single();
            Assert.AreEqual("p1", item.CreatorPlayerId);
            Assert.AreEqual("hat", item.ClothingTypeId);
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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>p1 hat</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", "hat", "<svg>p2 hat</svg>"));

            Assert.AreEqual(2, state.ClothingPool.Count);
            Assert.IsTrue(state.ClothingPool.Values.All(i => i.ClothingTypeId == "hat"));
            Assert.IsTrue(state.ClothingPool.Values.All(i => i.IsInPool));
        }

        [TestMethod]
        public async Task DrawingRoundState_ReadyFlagsResetBetweenRounds()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = "top", DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;
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

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = "hat",
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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = "top", DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            // Submit hats in round 0.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>p1 hat</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", "hat", "<svg>p2 hat</svg>"));

            // Advance to top round.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));
            Assert.AreEqual(1, state.CurrentDrawingClothingTypeIndex);

            // Submit tops in round 1.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "top", "<svg>p1 top</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", "top", "<svg>p2 top</svg>"));

            // Advance through top round → PoolReveal.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(4, state.ClothingPool.Count, "Pool should contain items from all drawing rounds.");
            Assert.AreEqual(2, state.ClothingPool.Values.Count(i => i.ClothingTypeId == "hat"),
                "Pool should contain 2 hat items.");
            Assert.AreEqual(2, state.ClothingPool.Values.Count(i => i.ClothingTypeId == "top"),
                "Pool should contain 2 top items.");
        }

        [TestMethod]
        public async Task PoolReveal_AllItemsAreInPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>p1</svg>"));
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p2", "hat", "<svg>p2</svg>"));

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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
                new() { Id = "top", DisplayName = "Top", MaxItemsPerRound = 3 },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "hat", "<svg>hat</svg>"));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // hat → top
            _engine.ProcessCommand(context, new SubmitDrawingCommand("p1", "top", "<svg>top</svg>"));
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // top → PoolReveal

            Assert.IsInstanceOfType<PoolRevealState>(context.Fsm.CurrentState);

            var byType = state.ClothingPool.Values
                .GroupBy(i => i.ClothingTypeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            Assert.IsTrue(byType.ContainsKey("hat"), "Pool should have items grouped under 'hat'.");
            Assert.IsTrue(byType.ContainsKey("top"), "Pool should have items grouped under 'top'.");
            Assert.AreEqual(1, byType["hat"].Count);
            Assert.AreEqual(1, byType["top"].Count);
        }

        [TestMethod]
        public async Task PoolReveal_ReadyCountUpdatesCorrectly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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

            var invalidConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
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

            // Skip to OutfitBuildingState.
            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.Phase);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_OnTimerExpiry_TransitionsToPool2Reveal()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // No players → no distinctness conflicts, so goes straight to Pool2RevealState.
            context.Fsm.TransitionTo(context, new OutfitCustomizationState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<Pool2RevealState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Pool2Reveal, state.Phase);
        }

        [TestMethod]
        public async Task VotingMatchupState_OnTimerExpiry_TransitionsToRoundResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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

            // Pre-fill all configured voting rounds so the next MarkReady ends the game.
            for (int i = 0; i < state.Config.VotingRounds; i++)
                state.VotingRounds.Add(new() { RoundNumber = i + 1 });

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            // Add a player and have them acknowledge the results.
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<FinalResultsState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Results, state.Phase);
        }

        [TestMethod]
        public async Task VotingRoundResultsState_MoreRoundsRemain_TransitionsToNextSetup()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Leave fewer completed rounds than configured (default 3).
            // Add 2 rounds, so one more should remain.
            state.VotingRounds.Add(new() { RoundNumber = 1 });
            state.VotingRounds.Add(new() { RoundNumber = 2 });

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

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

            state.VotingRounds.Add(new() { RoundNumber = 1 });
            context.Fsm.TransitionTo(context, new CoinFlipState());

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

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);

            // Non-host attempts to resume.
            _engine.ProcessCommand(context, new ResumeGameCommand("nonhost_id"));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task AbandonGame_FromDrawingRound_TransitionsToAbandonedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        [TestMethod]
        public async Task AbandonGame_FromPausedState_TransitionsToAbandonedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        [TestMethod]
        public async Task AbandonGame_FromFinalResults_TransitionsToAbandonedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new FinalResultsState());
            Assert.AreEqual(GamePhase.Results, state.Phase);

            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        [TestMethod]
        public async Task AbandonedState_AcceptsNoFurtherCommands()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));
            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);

            // Any subsequent command should be a no-op.
            _engine.ProcessCommand(context, new StartGameCommand(_host.Id));
            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        // ── Theme selection ───────────────────────────────────────────────────

        [TestMethod]
        public async Task LobbyState_StartGameCommand_ByNonHost_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            // Attempt to start from a non-host player ID.
            _engine.ProcessCommand(context, new StartGameCommand("nonhost_id"));

            // Should remain in lobby.
            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task ThemeSelectionState_HostPick_WaitsForSelectThemeCommand()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Force back to ThemeSelectionState with HostPick mode.
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.HostPick;
            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.ThemeSelection, state.Phase);

            // Now host selects a theme.
            _engine.ProcessCommand(context, new SelectThemeCommand(_host.Id, "retro_futurism"));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.AreEqual("retro_futurism", state.CurrentTheme?.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_Random_ImmediatelySelectsThemeAndAdvancesToDrawing()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Random source → immediately transitions to DrawingRoundState on entry.
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsFalse(string.IsNullOrEmpty(state.CurrentTheme.Id));
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_WaitsForAllSubmissions()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.PlayerWritten;

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
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Sci-Fi Noir"));
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p2", "Medieval Fantasy"));

            // Both submitted → should advance to DrawingRoundState with one of their themes.
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.CurrentTheme);
            var validThemes = new[] { "Sci-Fi Noir", "Medieval Fantasy" };
            CollectionAssert.Contains(validThemes, state.CurrentTheme.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_SubmissionsStoredInGameState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.PlayerWritten;

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
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.AreEqual(3, state.ThemeCandidates.Count);
            // Candidates should be distinct.
            var ids = state.ThemeCandidates.Select(t => t.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_AllPlayersVote_SelectsWinnerAndAdvances()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.RandomVoting;
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

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(winningCandidate.Id, state.CurrentTheme?.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_WaitsForAllVotes()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.RandomVoting;
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
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeAnnouncement.BeforeDrawing;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Theme should be revealed immediately in BeforeDrawing mode.
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsTrue(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeAnnouncement_AfterDrawing_ThemeNotRevealedDuringDrawing()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeAnnouncement.AfterDrawing;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Theme selected but NOT yet revealed to players.
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsFalse(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeAnnouncement_AfterDrawing_ThemeRevealedAfterDrawingCompletes()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeAnnouncement.AfterDrawing;
            // Use a single clothing type so one tick exhausts the drawing phase.
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];

            // Enter ThemeSelectionState → auto-selects theme but does not reveal.
            context.Fsm.TransitionTo(context, new ThemeSelectionState());
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

            // Set up two players sharing the same item.
            var sharedId = Guid.NewGuid();
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { ["hat"] = sharedId },
                }
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new()
                {
                    PlayerId = "p2",
                    SelectedItemsByType = new() { ["hat"] = sharedId },
                }
            };

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

            var updatedConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
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

            var updatedConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
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

            var invalidConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 1,     // below minimum of 30
                VotingRounds = 0,       // below minimum of 1
                BonusPointsForCompleteOutfit = -10, // negative
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand(_host.Id, invalidConfig));

            Assert.AreEqual(30, state.Config.DrawingTimeSec);
            Assert.AreEqual(1, state.Config.VotingRounds);
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

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var itemId = Guid.NewGuid();
            // Item created BY p1 – p1 must not be able to claim it via ClaimPoolItemCommand.
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p1",
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", itemId));

            Assert.IsNull(state.ClothingPool[itemId].ClaimedByPlayerId,
                "A player must not be able to claim an item they created.");
            Assert.IsFalse(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(itemId),
                "Self-drawn items must not be added to OwnedClothingItemIds via ClaimPoolItemCommand.");
        }

        [TestMethod]
        public async Task OutfitBuilding_ClaimPoolItem_AlreadyClaimed_SecondClaimFails()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = "hat",
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
            Assert.IsFalse(state.GamePlayers["p2"].OwnedClothingItemIds.Contains(itemId),
                "Losing claimer must not have the item in their owned list.");
        }

        [TestMethod]
        public async Task OutfitBuilding_ClaimPoolItem_Success_AddsToOwnedList()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = "hat",
                CreatorPlayerId = "p1",      // drawn by p1
                SvgContent = "<svg/>",
                IsInPool = true,
            };

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // p2 claims p1's hat – this is valid.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p2", itemId));

            Assert.AreEqual("p2", state.ClothingPool[itemId].ClaimedByPlayerId,
                "Claim by a different player must succeed.");
            Assert.IsTrue(state.GamePlayers["p2"].OwnedClothingItemIds.Contains(itemId),
                "Claimed item must appear in the claimer's OwnedClothingItemIds.");
        }

        [TestMethod]
        public async Task OutfitBuilding_UnclaimPoolItem_ReturnsToPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = "hat",
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
            Assert.IsFalse(state.GamePlayers["p2"].OwnedClothingItemIds.Contains(itemId),
                "Item must be removed from the unclaimer's OwnedClothingItemIds.");
        }

        [TestMethod]
        public async Task OutfitBuilding_UnclaimPoolItem_ByWrongPlayer_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                ClothingTypeId = "hat",
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

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = "hat",
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
            Assert.IsTrue(state.GamePlayers["p2"].OwnedClothingItemIds.Contains(itemId));
            Assert.IsFalse(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(itemId),
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

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            // p1 does NOT own this item (never claimed it).

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId }));

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

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p1",   // player owns it (self-drawn)
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // Submitting the hat item under the "top" slot – type mismatch.
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["top"] = hatId }));

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

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p2",   // drawn by another player
                SvgContent = "<svg/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit,
                "A valid outfit with owned items must be accepted.");
            Assert.AreEqual(hatId, state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType["hat"]);
        }

        // ── Outfit building – auto-fill ───────────────────────────────────────

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillsIncompleteOutfit()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = "hat",
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
            Assert.IsTrue(state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType.ContainsKey("hat"),
                "Auto-filled outfit must include the available hat slot.");
        }

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AutoFillPrefersNonSelfDrawnItems()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            var selfDrawnHat = Guid.NewGuid();
            state.ClothingPool[selfDrawnHat] = new()
            {
                Id = selfDrawnHat,
                ClothingTypeId = "hat",
                CreatorPlayerId = "p1",   // self-drawn
                SvgContent = "<svg self/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(selfDrawnHat);

            var claimedHat = Guid.NewGuid();
            state.ClothingPool[claimedHat] = new()
            {
                Id = claimedHat,
                ClothingTypeId = "hat",
                CreatorPlayerId = "p2",   // drawn by p2, claimed by p1
                SvgContent = "<svg other/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(claimedHat);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var chosen = state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType["hat"];
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
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var selfDrawnHat = Guid.NewGuid();
            state.ClothingPool[selfDrawnHat] = new()
            {
                Id = selfDrawnHat,
                ClothingTypeId = "hat",
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
                state.GamePlayers["p1"].SubmittedOutfit!.SelectedItemsByType["hat"]);
        }

        [TestMethod]
        public async Task OutfitBuilding_TimerExpiry_AlreadySubmittedOutfit_IsNotOverwritten()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                ClothingTypeId = "hat",
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
                ClaimedByPlayerId = "p1",
            };
            state.GamePlayers["p1"].OwnedClothingItemIds.Add(hatId);

            context.Fsm.TransitionTo(context, new OutfitBuildingState());

            // Player submits before the timer runs out.
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId }));

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
            Assert.AreEqual(1, state.GamePlayers.Count,
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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 3 },
            ];

            var player = new User("Alice", "alice1");
            state.RegisterPlayer(player);

            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Act: the registered player submits a drawing.
            var result = _engine.ProcessCommand(context,
                new SubmitDrawingCommand("alice1", "hat", "<svg/>"));

            // Assert: submission succeeds and the item is added to the pool.
            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(1, state.ClothingPool.Count,
                "One drawing should be in the pool after submission.");
            var item = state.ClothingPool.Values.Single();
            Assert.AreEqual("alice1", item.CreatorPlayerId);
            Assert.AreEqual("hat", item.ClothingTypeId);
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
                new() { Id = "hat", DisplayName = "Hat", MaxItemsPerRound = 1 },
            ];

            var player = new User("Alice", "alice1");
            state.RegisterPlayer(player);
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // First submission should succeed.
            _engine.ProcessCommand(context, new SubmitDrawingCommand("alice1", "hat", "<svg/>"));
            Assert.AreEqual(1, state.ClothingPool.Count);

            // Second submission must be rejected (limit = 1).
            _engine.ProcessCommand(context, new SubmitDrawingCommand("alice1", "hat", "<svg/>"));
            Assert.AreEqual(1, state.ClothingPool.Count,
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
        public async Task OutfitCustomizationState_SubmitWithoutSketch_SketchIsNull()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

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
                new SubmitCustomizationCommand("p1", "Outfit One"));
            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState,
                "Should still be in customization after only one player submits.");

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p2", "Outfit Two"));
            Assert.IsInstanceOfType<Pool2RevealState>(context.Fsm.CurrentState,
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

            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new() { ["hat"] = itemId },
                },
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());

            _engine.ProcessCommand(context,
                new SubmitCustomizationCommand("p1", "Hat Outfit", "<svg>overlay</svg>"));

            var submission = state.GamePlayers["p1"].SubmittedOutfit!;
            Assert.AreEqual("Hat Outfit", submission.Customization.OutfitName);
            Assert.AreEqual("<svg>overlay</svg>", submission.Customization.SketchSvgContent);
            Assert.IsTrue(submission.SelectedItemsByType.ContainsKey("hat"),
                "Original selected items must be preserved after customization.");
            Assert.AreEqual(itemId, submission.SelectedItemsByType["hat"]);
        }

        // ── Pool 2 Reveal state ───────────────────────────────────────────────

        [TestMethod]
        public async Task Pool2RevealState_OnEnter_SetsPool2RevealPhase()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new Pool2RevealState());

            Assert.AreEqual(GamePhase.Pool2Reveal, state.Phase);
            Assert.IsTrue(state.PhaseDeadlineUtc.HasValue,
                "Pool2RevealState must set a deadline on entry.");
        }

        [TestMethod]
        public async Task Pool2RevealState_TimerExpiry_AdvancesToOutfit2Building()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new Pool2RevealState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<Outfit2BuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Outfit2Building, state.Phase);
        }

        [TestMethod]
        public async Task Pool2RevealState_AllPlayersReady_AdvancesEarlyToOutfit2Building()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            context.Fsm.TransitionTo(context, new Pool2RevealState());
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<Outfit2BuildingState>(context.Fsm.CurrentState,
                "All-ready should advance early from Pool2Reveal to Outfit2Building.");
        }

        [TestMethod]
        public async Task Pool2RevealState_ClaimPoolItem_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId, ClothingTypeId = "hat", CreatorPlayerId = "p2",
                SvgContent = "<svg/>", IsInPool = true,
            };
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };

            context.Fsm.TransitionTo(context, new Pool2RevealState());
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", hatId));

            Assert.IsInstanceOfType<Pool2RevealState>(context.Fsm.CurrentState,
                "Pool reveal is view-only — a ClaimPoolItemCommand must not advance the state.");
            Assert.IsNull(state.ClothingPool[hatId].ClaimedByPlayerId,
                "Items must not be claimable during Pool2Reveal.");
        }

        [TestMethod]
        public async Task Pool2RevealState_OnEnter_ResetsPoolFromOutfit1Picks()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var usedHat = Guid.NewGuid();
            var freeHat = Guid.NewGuid();
            state.ClothingPool[usedHat] = new()
            {
                Id = usedHat, ClothingTypeId = "hat", CreatorPlayerId = "p2",
                SvgContent = "<svg/>", IsInPool = true,
            };
            state.ClothingPool[freeHat] = new()
            {
                Id = freeHat, ClothingTypeId = "hat", CreatorPlayerId = "p3",
                SvgContent = "<svg/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { ["hat"] = usedHat },
                },
            };

            context.Fsm.TransitionTo(context, new Pool2RevealState());

            Assert.IsFalse(state.ClothingPool[usedHat].IsInPool,
                "Item selected in Outfit 1 must be removed from the Outfit 2 pool during Pool2Reveal.");
            Assert.IsTrue(state.ClothingPool[freeHat].IsInPool,
                "Item not selected in any Outfit 1 must remain in the Outfit 2 pool.");
        }

        // ── Outfit 2: pool reset on entry ─────────────────────────────────────

        [TestMethod]
        public async Task Outfit2Building_OnEnter_RemovesOutfit1PicksFromPool()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Two items in the pool: one selected in Outfit 1, one not.
            var usedHat = Guid.NewGuid();
            var unusedHat = Guid.NewGuid();
            state.ClothingPool[usedHat] = new()
            {
                Id = usedHat,
                ClothingTypeId = "hat",
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.ClothingPool[unusedHat] = new()
            {
                Id = unusedHat,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new() { ["hat"] = usedHat },
                },
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            Assert.IsFalse(state.ClothingPool[usedHat].IsInPool,
                "Item selected in Outfit 1 must be removed from the Outfit 2 pool.");
            Assert.IsTrue(state.ClothingPool[unusedHat].IsInPool,
                "Item not selected in any Outfit 1 must remain in the Outfit 2 pool.");
        }

        [TestMethod]
        public async Task Outfit2Building_OnEnter_ClearsAllClaims()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // A previously claimed item.
            var itemId = Guid.NewGuid();
            state.ClothingPool[itemId] = new()
            {
                Id = itemId,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new Dictionary<string, Guid>(), // different item selected
                },
                OwnedClothingItemIds = [itemId],
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            Assert.IsNull(state.ClothingPool[itemId].ClaimedByPlayerId,
                "All claims must be cleared when Outfit 2 building begins.");
        }

        [TestMethod]
        public async Task Outfit2Building_OnEnter_SelfDrawnItemsInPool_RemainsOwned()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // p1 drew a hat that is NOT in their Outfit 1, so it remains in the pool.
            var selfHat = Guid.NewGuid();
            state.ClothingPool[selfHat] = new()
            {
                Id = selfHat,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new Dictionary<string, Guid>(), // selfHat not selected
                },
                OwnedClothingItemIds = [selfHat],
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            Assert.IsTrue(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(selfHat),
                "A self-drawn item that is still in the pool must remain in the player's owned set.");
        }

        [TestMethod]
        public async Task Outfit2Building_OnEnter_Outfit1SelfDrawnItemUsed_NotInOwned()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // p1 drew a hat AND used it in Outfit 1; it must be excluded from Outfit 2 pool.
            var selfHat = Guid.NewGuid();
            state.ClothingPool[selfHat] = new()
            {
                Id = selfHat,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new() { ["hat"] = selfHat },
                },
                OwnedClothingItemIds = [selfHat],
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            Assert.IsFalse(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(selfHat),
                "A self-drawn item used in Outfit 1 must not be in the player's Outfit 2 owned set " +
                "when CanReuseOutfit1Items is false.");
        }

        // ── Outfit 2: CanReuseOutfit1Items ────────────────────────────────────

        [TestMethod]
        public async Task Outfit2Building_CanReuseOutfit1Items_True_AddsBackOutfit1Picks()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new() { ["hat"] = hatId },
                },
                OwnedClothingItemIds = [hatId],
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            // The hat was in Outfit 1 (now IsInPool=false), but CanReuseOutfit1Items allows it back.
            Assert.IsTrue(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(hatId),
                "When CanReuseOutfit1Items is true the player's own Outfit 1 picks must remain owned.");
        }

        [TestMethod]
        public async Task Outfit2Building_CanReuseOutfit1Items_False_DoesNotAddBackOutfit1Picks()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.CanReuseOutfit1Items = false;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new() { ["hat"] = hatId },
                },
                OwnedClothingItemIds = [hatId],
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            Assert.IsFalse(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(hatId),
                "When CanReuseOutfit1Items is false the player's Outfit 1 picks must not be owned for Outfit 2.");
        }

        // ── Outfit 2: claim / unclaim ─────────────────────────────────────────

        [TestMethod]
        public async Task Outfit2Building_ClaimPoolItem_Success_AddsToOwnedList()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = "hat",
                CreatorPlayerId = "p2",
                SvgContent = "<svg/>",
                IsInPool = true,
            };
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", hatId));

            Assert.AreEqual("p1", state.ClothingPool[hatId].ClaimedByPlayerId);
            Assert.IsTrue(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(hatId));
        }

        [TestMethod]
        public async Task Outfit2Building_ClaimPoolItem_ItemRemovedByReset_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId,
                ClothingTypeId = "hat",
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
                    SelectedItemsByType = new() { ["hat"] = hatId },
                },
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            // hatId was in Outfit 1 → IsInPool = false → claim must be rejected.
            _engine.ProcessCommand(context, new ClaimPoolItemCommand("p1", hatId));

            Assert.IsNull(state.ClothingPool[hatId].ClaimedByPlayerId,
                "An item removed from the Outfit 2 pool must not be claimable.");
            Assert.IsFalse(state.GamePlayers["p1"].OwnedClothingItemIds.Contains(hatId));
        }

        // ── Outfit 2: submit validation ───────────────────────────────────────

        [TestMethod]
        public async Task Outfit2Building_SubmitOutfit_ValidAndDistinct_IsAccepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var outfit1Hat = Guid.NewGuid();
            var outfit2Hat = Guid.NewGuid();

            state.ClothingPool[outfit1Hat] = new()
            {
                Id = outfit1Hat, ClothingTypeId = "hat", CreatorPlayerId = "p2", SvgContent = "<svg/>", IsInPool = false,
            };
            // outfit2Hat is self-drawn by p1 → after pool reset it will be auto-owned.
            state.ClothingPool[outfit2Hat] = new()
            {
                Id = outfit2Hat, ClothingTypeId = "hat", CreatorPlayerId = "p1", SvgContent = "<svg/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { ["hat"] = outfit1Hat },
                },
            };

            // Pool reset: outfit1Hat excluded (in Outfit 1 picks); outfit2Hat stays (self-drawn by p1).
            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = outfit2Hat }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "A valid, distinct Outfit 2 submission must be accepted.");
            Assert.AreEqual(outfit2Hat, state.GamePlayers["p1"].SubmittedOutfit2!.SelectedItemsByType["hat"]);
        }

        [TestMethod]
        public async Task Outfit2Building_SubmitOutfit_ViolatesDistinctness_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
                new() { Id = "top", DisplayName = "Top" },
                new() { Id = "shoes", DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            // Allow reuse so the player still owns their Outfit 1 picks in Outfit 2.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            foreach (var (id, type) in new[] { (hatId, "hat"), (topId, "top"), (shoesId, "shoes") })
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
                    SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
                },
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId }));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "An Outfit 2 that shares 3+ items with any Outfit 1 must be rejected.");
        }

        [TestMethod]
        public async Task Outfit2Building_SubmitOutfit_MatchesOtherPlayersOutfit1_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
                new() { Id = "top", DisplayName = "Top" },
                new() { Id = "shoes", DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            foreach (var (id, type) in new[] { (hatId, "hat"), (topId, "top"), (shoesId, "shoes") })
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
                    SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
                },
            };

            // Pool reset removes hatId/topId/shoesId (in p2's Outfit 1) from pool.
            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            // Manually grant p1 access to those items to isolate the distinctness-check logic.
            // (In game play this could happen if an item appears in multiple Outfit 1s via
            // CanReuseOutfit1Items, but here we directly test that the cross-player check fires.)
            state.GamePlayers["p1"].OwnedClothingItemIds.AddRange([hatId, topId, shoesId]);

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId }));

            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "An Outfit 2 must be rejected when it is too similar to another player's Outfit 1.");
        }

        [TestMethod]
        public async Task Outfit2Building_SubmitOutfit_DistinctnessDisabled_AllowsSimilarOutfit()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
                new() { Id = "top", DisplayName = "Top" },
                new() { Id = "shoes", DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 0; // disabled
            // Allow reuse so the player still owns their Outfit 1 picks in Outfit 2.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var shoesId = Guid.NewGuid();

            foreach (var (id, type) in new[] { (hatId, "hat"), (topId, "top"), (shoesId, "shoes") })
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
                    SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
                },
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "When distinctness is disabled (threshold=0) identical outfits must be accepted.");
        }

        [TestMethod]
        public async Task Outfit2Building_SubmitOutfit_BelowThreshold_IsAccepted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
                new() { Id = "top", DisplayName = "Top" },
                new() { Id = "shoes", DisplayName = "Shoes" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 3;
            // CanReuseOutfit1Items so hatId/topId/outfit1Shoes are re-owned after pool reset.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();
            var outfit1Shoes = Guid.NewGuid();
            // outfit2Shoes is self-drawn by p1 so it will be auto-owned after reset.
            var outfit2Shoes = Guid.NewGuid();

            foreach (var (id, type) in new[]
            {
                (hatId, "hat"), (topId, "top"), (outfit1Shoes, "shoes"),
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
                Id = outfit2Shoes, ClothingTypeId = "shoes", CreatorPlayerId = "p1",
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
                    SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = outfit1Shoes },
                },
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatId, ["top"] = topId, ["shoes"] = outfit2Shoes }));

            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "Outfit 2 sharing fewer than the threshold items with Outfit 1 must be accepted.");
        }

        // ── Outfit 2: early advance ───────────────────────────────────────────

        [TestMethod]
        public async Task Outfit2Building_AllPlayersSubmit_AdvancesToVoting()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 0; // disable to simplify
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var hatA = Guid.NewGuid();
            var hatB = Guid.NewGuid();
            state.ClothingPool[hatA] = new()
            {
                Id = hatA, ClothingTypeId = "hat", CreatorPlayerId = "p2", SvgContent = "<svg/>", IsInPool = true,
            };
            state.ClothingPool[hatB] = new()
            {
                Id = hatB, ClothingTypeId = "hat", CreatorPlayerId = "p1", SvgContent = "<svg/>", IsInPool = true,
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

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p1",
                new Dictionary<string, Guid> { ["hat"] = hatB }));
            Assert.IsInstanceOfType<Outfit2BuildingState>(context.Fsm.CurrentState,
                "Should not advance until all players have submitted Outfit 2.");

            _engine.ProcessCommand(context, new SubmitOutfitCommand("p2",
                new Dictionary<string, Guid> { ["hat"] = hatA }));
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState,
                "Should advance to voting once all players submit Outfit 2.");
        }

        // ── Outfit 2: timer expiry auto-fill ──────────────────────────────────

        [TestMethod]
        public async Task Outfit2Building_TimerExpiry_AutoFillsIncompleteOutfit2()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 0; // disable to keep auto-fill simple
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Hat is self-drawn by p1 and not selected in p1's Outfit 1 →
            // after pool reset it stays in pool and is auto-owned by p1.
            var hatId = Guid.NewGuid();
            state.ClothingPool[hatId] = new()
            {
                Id = hatId, ClothingTypeId = "hat", CreatorPlayerId = "p1", SvgContent = "<svg/>", IsInPool = true,
            };
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new() { PlayerId = "p1", SelectedItemsByType = new() }, // empty Outfit 1
            };

            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            Assert.IsNull(state.GamePlayers["p1"].SubmittedOutfit2);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.GamePlayers["p1"].SubmittedOutfit2,
                "Auto-fill must produce an Outfit 2 when the timer expires.");
            Assert.IsTrue(state.GamePlayers["p1"].SubmittedOutfit2!.SelectedItemsByType.ContainsKey("hat"));
        }

        [TestMethod]
        public async Task Outfit2Building_TimerExpiry_AutoFillPrefersNonConflictingItems()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ClothingTypes =
            [
                new() { Id = "hat", DisplayName = "Hat" },
            ];
            state.Config.Outfit2DistinctnessThreshold = 1; // any shared item is a violation
            // Allow reuse so p1 owns both conflictHat and distinctHat after reset.
            state.Config.CanReuseOutfit1Items = true;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var conflictHat = Guid.NewGuid(); // also in p1's Outfit 1
            var distinctHat = Guid.NewGuid(); // not in any Outfit 1

            // conflictHat was used in p1's Outfit 1 → will be excluded from pool after reset
            // but CanReuseOutfit1Items will add it back to p1's owned set.
            // distinctHat is self-drawn by p1 and not in any Outfit 1 → stays in pool.
            state.ClothingPool[conflictHat] = new()
            {
                Id = conflictHat, ClothingTypeId = "hat", CreatorPlayerId = "p2",
                SvgContent = "<svg conflict/>", IsInPool = true,
            };
            state.ClothingPool[distinctHat] = new()
            {
                Id = distinctHat, ClothingTypeId = "hat", CreatorPlayerId = "p1",
                SvgContent = "<svg distinct/>", IsInPool = true,
            };

            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { ["hat"] = conflictHat },
                },
            };

            // After pool reset:
            //   conflictHat: IsInPool=false (in p1's Outfit 1); re-added to p1's owned via CanReuseOutfit1Items.
            //   distinctHat: IsInPool=true (not in Outfit 1); auto-owned (self-drawn by p1).
            // Auto-fill prefers distinctHat because it doesn't appear in any Outfit 1.
            context.Fsm.TransitionTo(context, new Outfit2BuildingState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            var chosenHat = state.GamePlayers["p1"].SubmittedOutfit2!.SelectedItemsByType["hat"];
            Assert.AreEqual(distinctHat, chosenHat,
                "Auto-fill must prefer an item that does not appear in any Outfit 1 over one that does.");
        }

        // ── OutfitDistinctnessEvaluator unit tests ────────────────────────────

        [TestMethod]
        public void OutfitDistinctnessEvaluator_CountSharedItems_NoOverlap_ReturnsZero()
        {
            var hat1 = Guid.NewGuid();
            var hat2 = Guid.NewGuid();

            var outfit1 = new OutfitSubmission { SelectedItemsByType = new() { ["hat"] = hat1 } };
            var outfit2 = new OutfitSubmission { SelectedItemsByType = new() { ["hat"] = hat2 } };

            Assert.AreEqual(0, OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2));
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_CountSharedItems_FullOverlap_ReturnsCount()
        {
            var hatId = Guid.NewGuid();
            var topId = Guid.NewGuid();

            var outfit1 = new OutfitSubmission
            {
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId },
            };

            Assert.AreEqual(2, OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2));
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_CountSharedItems_SameSlotDifferentItem_IsZero()
        {
            var hatA = Guid.NewGuid();
            var hatB = Guid.NewGuid();

            var outfit1 = new OutfitSubmission { SelectedItemsByType = new() { ["hat"] = hatA } };
            var outfit2 = new OutfitSubmission { SelectedItemsByType = new() { ["hat"] = hatB } };

            Assert.AreEqual(0, OutfitDistinctnessEvaluator.CountSharedItems(outfit1, outfit2),
                "Items in the same slot but with different IDs must not count as shared.");
        }

        [TestMethod]
        public void OutfitDistinctnessEvaluator_ViolatesDistinctnessRule_Disabled_ReturnsFalse()
        {
            var hatId = Guid.NewGuid();
            var outfit1 = new OutfitSubmission { SelectedItemsByType = new() { ["hat"] = hatId } };
            var outfit2 = new OutfitSubmission { SelectedItemsByType = new() { ["hat"] = hatId } };

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
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
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
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = Guid.NewGuid() },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = newShoes },
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
                SelectedItemsByType = new() { ["hat"] = Guid.NewGuid(), ["top"] = Guid.NewGuid() },
            };
            var outfit1B = new OutfitSubmission
            {
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
            };
            var outfit2 = new OutfitSubmission
            {
                SelectedItemsByType = new() { ["hat"] = hatId, ["top"] = topId, ["shoes"] = shoesId },
            };

            Assert.IsTrue(
                OutfitDistinctnessEvaluator.ViolatesDistinctnessRule(outfit2, [outfit1A, outfit1B], threshold: 3),
                "Violating distinctness against any single Outfit 1 must be detected even when other Outfit 1s are fine.");
        }
    }
}
