using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.State.Games.DrawnToDress;
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
        public async Task OutfitCustomizationState_OnTimerExpiry_TransitionsToVotingSetup()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // No players → no distinctness conflicts, so goes straight to VotingRoundSetupState
            // which chains to VotingMatchupState.
            context.Fsm.TransitionTo(context, new OutfitCustomizationState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Voting, state.Phase);
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
    }
}
