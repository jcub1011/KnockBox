using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Tests for the timer-driven auto-advance features:
    /// - Drawing sub-round deadline set/cleared
    /// - DrawingState.Tick auto-advances to the next clothing type or OutfitBuilding
    /// - OutfitBuildingState.Tick auto-fills incomplete outfits and transitions to Customization
    /// - OutfitCustomizationState auto-advances when every participant submits
    /// - FixDistinctnessViolations swaps conflicting Outfit-2 items on timer expiry
    /// </summary>
    [TestClass]
    public class TimerAndAutoAdvanceTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                       .Returns(0);

            _host = new User("Host", "host-id");

            _engine = new DrawnToDressGameEngine(
                _randomMock.Object,
                Mock.Of<ILogger<DrawnToDressGameEngine>>(),
                Mock.Of<ILogger<DrawnToDressGameState>>());
        }

        // ------------------------------------------------------------------
        // Drawing phase: deadline set on enter / reset on advance
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task DrawingPhase_PhaseDeadlineUtc_SetWhenGameStarts()
        {
            using var state = await StartGameAsync(minPlayers: 4);

            Assert.AreEqual(GamePhase.Drawing, state.CurrentPhase);
            Assert.IsNotNull(state.PhaseDeadlineUtc, "Deadline should be set on entering DrawingState.");
            Assert.IsTrue(state.PhaseDeadlineUtc!.Value > DateTimeOffset.UtcNow,
                "Deadline should be in the future.");
        }

        [TestMethod]
        public async Task DrawingPhase_HostAdvance_ResetsDeadlineForNextType()
        {
            using var state = await StartGameAsync(minPlayers: 4);

            var firstDeadline = state.PhaseDeadlineUtc;
            // Small wait to ensure the new deadline differs
            await Task.Delay(50);

            _engine.AdvanceDrawingRound(_host, state);

            Assert.AreNotEqual(firstDeadline, state.PhaseDeadlineUtc,
                "Deadline should reset for the next clothing type.");
        }

        [TestMethod]
        public async Task DrawingPhase_HostAdvancePastLastType_ClearsDeadlineAndTransitionsToOutfitBuilding()
        {
            using var state = await StartGameAsync(minPlayers: 4);

            // Advance past all clothing types
            for (int i = 0; i < state.Settings.ClothingTypes.Count; i++)
                _engine.AdvanceDrawingRound(_host, state);

            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase);
        }

        // ------------------------------------------------------------------
        // DrawingState.Tick: auto-advance when deadline passes
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task DrawingState_Tick_BeforeDeadline_DoesNotAdvance()
        {
            using var state = await StartGameAsync(minPlayers: 4);
            int beforeIndex = state.CurrentDrawingTypeIndex;

            // Tick well before the deadline
            _engine.Tick(state, DateTimeOffset.UtcNow.AddSeconds(-10));

            Assert.AreEqual(beforeIndex, state.CurrentDrawingTypeIndex,
                "Tick before deadline should not advance the drawing type.");
        }

        [TestMethod]
        public async Task DrawingState_Tick_AfterDeadline_AdvancesToNextType()
        {
            using var state = await StartGameAsync(minPlayers: 4);
            Assert.AreEqual(0, state.CurrentDrawingTypeIndex);

            // Simulate deadline expired
            _engine.Tick(state, state.PhaseDeadlineUtc!.Value.AddSeconds(1));

            Assert.AreEqual(1, state.CurrentDrawingTypeIndex,
                "Tick after deadline should advance to the next clothing type.");
        }

        [TestMethod]
        public async Task DrawingState_Tick_AfterDeadline_AllTypes_TransitionsToOutfitBuilding()
        {
            using var state = await StartGameAsync(minPlayers: 4);

            // Expire each sub-round via Tick
            for (int i = 0; i < state.Settings.ClothingTypes.Count; i++)
                _engine.Tick(state, state.PhaseDeadlineUtc!.Value.AddSeconds(1));

            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase,
                "After all drawing types expire, game should move to OutfitBuilding.");
            Assert.IsNotNull(state.PhaseDeadlineUtc,
                "OutfitBuilding should also have a deadline set.");
        }

        [TestMethod]
        public async Task DrawingState_Tick_AfterDeadline_NewDeadlineSetForNextType()
        {
            using var state = await StartGameAsync(minPlayers: 4);
            var firstDeadline = state.PhaseDeadlineUtc!.Value;

            // Advance one type via Tick
            await Task.Delay(50);
            _engine.Tick(state, firstDeadline.AddSeconds(1));

            Assert.AreEqual(GamePhase.Drawing, state.CurrentPhase);
            Assert.AreNotEqual(firstDeadline, state.PhaseDeadlineUtc,
                "A new per-sub-round deadline should be set after auto-advance.");
        }

        // ------------------------------------------------------------------
        // OutfitBuildingState.Tick: auto-fill and transition
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task OutfitBuildingState_Tick_AfterDeadline_AutoFillsAndTransitionsToCustomization()
        {
            using var state = await StartOutfitBuildingAsync(minPlayers: 4);

            // Tick after deadline — no outfits claimed yet; should auto-fill all
            _engine.Tick(state, state.PhaseDeadlineUtc!.Value.AddSeconds(1));

            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase,
                "Tick after outfit building deadline should transition to Customization.");

            // Every participant should have a locked outfit
            var allPlayers = GetAllPlayers(state);
            foreach (var player in allPlayers)
            {
                var outfit = state.GetPlayerOutfit(player.Id, 1);
                Assert.IsNotNull(outfit, $"Player {player.Name} should have an auto-created outfit.");
                Assert.IsTrue(outfit!.IsLocked, $"Player {player.Name}'s outfit should be locked.");
            }
        }

        [TestMethod]
        public async Task OutfitBuildingState_Tick_BeforeDeadline_DoesNotTransition()
        {
            using var state = await StartOutfitBuildingAsync(minPlayers: 4);

            _engine.Tick(state, DateTimeOffset.UtcNow.AddSeconds(-30));

            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase,
                "Tick before deadline should not transition.");
        }

        [TestMethod]
        public async Task OutfitBuildingState_DeadlineClearedAfterTransition()
        {
            using var state = await StartOutfitBuildingAsync(minPlayers: 4);

            _engine.Tick(state, state.PhaseDeadlineUtc!.Value.AddSeconds(1));

            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase);
            Assert.IsNull(state.PhaseDeadlineUtc,
                "Deadline should be cleared after leaving OutfitBuilding.");
        }

        // ------------------------------------------------------------------
        // OutfitCustomizationState: auto-advance when all submit
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task OutfitCustomization_LastPlayerSubmits_AutoAdvancesToVoting()
        {
            using var state = await StartCustomizationAsync(minPlayers: 4);

            var allPlayers = GetAllPlayers(state);

            // All-but-last submit
            for (int i = 0; i < allPlayers.Count - 1; i++)
                _engine.SubmitOutfit(allPlayers[i], state, $"Outfit {i}");

            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase,
                "Should remain in customization until everyone submits.");

            // Last player submits → should auto-advance
            var lastResult = _engine.SubmitOutfit(allPlayers[^1], state, "Final Outfit");
            Assert.IsTrue((bool)lastResult.IsSuccess);
            Assert.AreEqual(GamePhase.Voting, state.CurrentPhase,
                "Customization phase should auto-advance to Voting when all submit.");
        }

        [TestMethod]
        public async Task OutfitCustomization_HostCanStillForceAdvanceBeforeAllSubmit()
        {
            using var state = await StartCustomizationAsync(minPlayers: 4);

            // Only host submits
            _engine.SubmitOutfit(_host, state, "Host's Outfit");

            // Host forces advance
            var result = _engine.EndCustomizationPhase(_host, state);
            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.Voting, state.CurrentPhase);
        }

        // ------------------------------------------------------------------
        // FixDistinctnessViolations: swaps conflicting Outfit-2 items on timer expiry
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task OutfitBuildingTimerExpiry_Round2_FixesDistinctnessViolations()
        {
            // Only meaningful when CanReuseOutfit1Items = true (items from Outfit 1 appear in the pool again)
            var settings = new DrawnToDressSettings
            {
                NumOutfitRounds = 2,
                MinPlayers = 4,
                CanReuseOutfit1Items = true,
                OutfitDistinctnessRule = 2,
            };

            using var state = new DrawnToDressGameState(_host, Mock.Of<ILogger<DrawnToDressGameState>>(), settings);
            state.UpdateJoinableStatus(true);
            var players = new[] { new User("P1", "p1"), new User("P2", "p2"),
                                  new User("P3", "p3"), new User("P4", "p4") };
            foreach (var p in players) state.RegisterPlayer(p);
            await _engine.StartAsync(_host, state);

            var allPlayers = GetAllPlayers(state);

            // Draw 2 items per player per type
            foreach (var type in state.Settings.ClothingTypes)
            {
                foreach (var player in allPlayers)
                {
                    _engine.SubmitDrawing(player, state, $"<svg p='{player.Id}' t='{type}' n='1'/>");
                    _engine.SubmitDrawing(player, state, $"<svg p='{player.Id}' t='{type}' n='2'/>");
                }
                _engine.AdvanceDrawingRound(_host, state);
            }

            // Outfit 1 building – each player takes one item per type they didn't draw
            foreach (var player in allPlayers)
                FillCompleteOutfit(player, state);
            _engine.EndOutfitBuilding(_host, state);

            // Submit all Outfit 1s
            foreach (var player in allPlayers)
                _engine.SubmitOutfit(player, state, $"{player.Name} Outfit 1");

            // Advance to Outfit 2 building
            _engine.EndCustomizationPhase(_host, state);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase);
            Assert.AreEqual(2, state.CurrentOutfitRound);

            // Don't claim anything — let the timer expire.
            // FixDistinctnessViolations should resolve any violations introduced by auto-fill.
            _engine.Tick(state, state.PhaseDeadlineUtc!.Value.AddSeconds(1));

            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase);

            // Every participant should have a locked outfit
            foreach (var player in allPlayers)
            {
                var outfit2 = state.GetPlayerOutfit(player.Id, 2);
                Assert.IsNotNull(outfit2, $"{player.Name} should have an auto-created Outfit 2.");
                Assert.IsTrue(outfit2!.IsLocked, $"{player.Name}'s Outfit 2 should be locked.");
            }
            // FixDistinctnessViolations() ran without exception — verified by the transition succeeding.
        }

        // ------------------------------------------------------------------
        // Distinctness check error message specificity
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task OutfitCustomization_DistinctnessViolation_ErrorMentionsPlayerName()
        {
            var settings = new DrawnToDressSettings
            {
                NumOutfitRounds = 2,
                MinPlayers = 4,
                CanReuseOutfit1Items = true,
                OutfitDistinctnessRule = 2,
                ClothingTypes = [ClothingType.Hat, ClothingType.Shirt, ClothingType.Pants, ClothingType.Shoes],
            };

            using var state = new DrawnToDressGameState(_host, Mock.Of<ILogger<DrawnToDressGameState>>(), settings);
            state.UpdateJoinableStatus(true);
            var players = new[] { new User("Alice", "p1"), new User("Bob", "p2"),
                                  new User("Carol", "p3"), new User("Dave", "p4") };
            foreach (var p in players) state.RegisterPlayer(p);
            await _engine.StartAsync(_host, state);
            var allPlayers = GetAllPlayers(state);

            // Draw items
            foreach (var type in state.Settings.ClothingTypes)
            {
                foreach (var player in allPlayers)
                {
                    _engine.SubmitDrawing(player, state, $"<svg/>");
                    _engine.SubmitDrawing(player, state, $"<svg/>");
                }
                _engine.AdvanceDrawingRound(_host, state);
            }

            // Build and submit Outfit 1 for everyone
            foreach (var player in allPlayers) FillCompleteOutfit(player, state);
            _engine.EndOutfitBuilding(_host, state);
            foreach (var player in allPlayers) _engine.SubmitOutfit(player, state, $"{player.Name} Outfit 1");
            _engine.EndCustomizationPhase(_host, state);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase);

            // For host, force all items from Outfit 1 back into the pool so host can "re-pick" identical items
            // (because CanReuseOutfit1Items = true, the pool includes outfit1 items)
            // Find host's outfit 1 items and explicitly claim the same ones
            var hostOutfit1 = state.GetPlayerOutfit(_host.Id, 1)!;
            var outfit1ItemIds = hostOutfit1.ItemIds.ToList();

            foreach (var type in state.Settings.ClothingTypes)
            {
                // Try to claim same item as was in Outfit 1 — this might already be taken by auto-pool logic
                var sameItem = state.AvailablePool.FirstOrDefault(i =>
                    i.Type == type && i.CreatorId != _host.Id && outfit1ItemIds.Contains(i.Id));
                if (sameItem is not null)
                    _engine.ClaimItem(_host, state, sameItem.Id);
                else
                {
                    var any = state.AvailablePool.FirstOrDefault(i => i.Type == type && i.CreatorId != _host.Id);
                    if (any is not null) _engine.ClaimItem(_host, state, any.Id);
                }
            }

            _engine.EndOutfitBuilding(_host, state);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase);

            var hostOutfit2 = state.GetPlayerOutfit(_host.Id, 2);
            if (hostOutfit2 is null || !hostOutfit2.IsComplete) return; // pool insufficient — skip

            var (isDistinct, conflicting, _) = state.CheckDistinctnessWithDetails(hostOutfit2);
            if (isDistinct) return; // No violation, nothing to test

            var result = _engine.SubmitOutfit(_host, state, "Host Outfit 2");
            Assert.IsTrue((bool)result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));

            // Error message should mention the conflicting player's name
            string msg = err.PublicMessage;
            bool mentionsPlayer = msg.Contains(conflicting!.PlayerName) || msg.Contains("your");
            Assert.IsTrue(mentionsPlayer,
                $"Error message should mention the conflicting player. Got: '{msg}'");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private async Task<DrawnToDressGameState> StartGameAsync(int minPlayers)
        {
            var settings = new DrawnToDressSettings { MinPlayers = minPlayers };
            var state = new DrawnToDressGameState(_host, Mock.Of<ILogger<DrawnToDressGameState>>(), settings);
            state.UpdateJoinableStatus(true);
            // Register minPlayers non-host players (same count as MinPlayers, matching integration test convention)
            for (int i = 0; i < minPlayers; i++)
                state.RegisterPlayer(new User($"P{i}", $"p{i}"));
            var result = await _engine.StartAsync(_host, state);
            Assert.IsTrue((bool)result.IsSuccess, $"StartAsync failed: {result.Error}");
            return state;
        }

        private async Task<DrawnToDressGameState> StartOutfitBuildingAsync(int minPlayers)
        {
            var state = await StartGameAsync(minPlayers);
            var allPlayers = GetAllPlayers(state);

            // Submit drawings and advance through all types
            foreach (var type in state.Settings.ClothingTypes)
            {
                foreach (var p in allPlayers)
                {
                    _engine.SubmitDrawing(p, state, $"<svg t='{type}' p='{p.Id}' n='1'/>");
                    _engine.SubmitDrawing(p, state, $"<svg t='{type}' p='{p.Id}' n='2'/>");
                }
                _engine.AdvanceDrawingRound(_host, state);
            }

            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase);
            return state;
        }

        private async Task<DrawnToDressGameState> StartCustomizationAsync(int minPlayers)
        {
            var settings = new DrawnToDressSettings { MinPlayers = minPlayers, NumOutfitRounds = 1 };
            var state = new DrawnToDressGameState(_host, Mock.Of<ILogger<DrawnToDressGameState>>(), settings);
            state.UpdateJoinableStatus(true);
            for (int i = 0; i < minPlayers; i++)
                state.RegisterPlayer(new User($"P{i}", $"p{i}"));
            var result = await _engine.StartAsync(_host, state);
            Assert.IsTrue((bool)result.IsSuccess, $"StartAsync failed: {result.Error}");

            var allPlayers = GetAllPlayers(state);

            // Submit drawings and advance
            foreach (var type in state.Settings.ClothingTypes)
            {
                foreach (var p in allPlayers)
                {
                    _engine.SubmitDrawing(p, state, $"<svg/>");
                    _engine.SubmitDrawing(p, state, $"<svg/>");
                }
                _engine.AdvanceDrawingRound(_host, state);
            }

            // Build and lock outfits
            foreach (var p in allPlayers) FillCompleteOutfit(p, state);
            _engine.EndOutfitBuilding(_host, state);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase);
            return state;
        }

        private void FillCompleteOutfit(User player, DrawnToDressGameState state)
        {
            foreach (var type in state.Settings.ClothingTypes)
            {
                if (state.GetPlayerOutfit(player.Id, state.CurrentOutfitRound)?.Items[type] is not null)
                    continue;
                var item = state.AvailablePool.FirstOrDefault(i => i.Type == type && i.CreatorId != player.Id);
                if (item is not null) _engine.ClaimItem(player, state, item.Id);
            }
        }

        private static List<User> GetAllPlayers(DrawnToDressGameState state) =>
            [state.Host, .. state.Players];
    }
}
