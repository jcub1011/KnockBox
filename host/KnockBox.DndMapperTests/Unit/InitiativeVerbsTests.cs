using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class InitiativeVerbsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            // Sequential RNG: every d20 returns 10.
            (_engine, _state, _host, _) = EngineTestFactory.Build(10, 10, 10, 10, 10, 10, 10, 10);
            // Seed a map so player auto-spawn during initiative start can locate tokens.
            var m = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(m.TryGetSuccess(out var mapId));
            _engine.SetActiveMapAsync(_state, _host, mapId);
        }

        private Guid SeedNpcToken(string name = "Orc")
        {
            var mapId = _state.ActiveMapId!.Value;
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, name);
            Assert.IsTrue(spawn.TryGetSuccess(out var id));
            return id;
        }

        [TestMethod]
        public void Start_HostOnly()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.StartInitiativeAsync(_state, player, []);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void Start_WithPlayersAndNpcs_CreatesWaitingPhase()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var npcId = SeedNpcToken();
            var r = _engine.StartInitiativeAsync(_state, _host, [npcId]);
            Assert.IsTrue(r.IsSuccess);
            Assert.IsNotNull(_state.ActiveCombat);
            Assert.AreEqual(CombatPhase.WaitingForRolls, _state.ActiveCombat!.Phase);
            Assert.HasCount(2, _state.ActiveCombat.TurnOrder);
        }

        [TestMethod]
        public void Start_EmptyTurnOrder_Rejected()
        {
            var r = _engine.StartInitiativeAsync(_state, _host, []);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void Start_AlreadyActive_Rejected()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            _engine.StartInitiativeAsync(_state, _host, []);
            var second = _engine.StartInitiativeAsync(_state, _host, []);
            Assert.IsTrue(second.IsFailure);
        }

        [TestMethod]
        public void SubmitInitiativeRoll_AppendsRollLog_AndAdvancesTransition()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            _engine.StartInitiativeAsync(_state, _host, []);

            var r = _engine.SubmitInitiativeRollAsync(_state, player);
            Assert.IsTrue(r.IsSuccess);
            // Only one combatant — auto-transition fires.
            Assert.AreEqual(CombatPhase.Active, _state.ActiveCombat!.Phase);
            Assert.Contains(rr => rr.Label == "Initiative" && rr.RollerUserId == player.Id, _state.RollLog);
        }

        [TestMethod]
        public void ForceInitiativeRoll_LogsForcedByHost_AndMarksFlag()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var npcId = SeedNpcToken();
            _engine.StartInitiativeAsync(_state, _host, [npcId]);

            var playerEntryId = _state.ActiveCombat!.TurnOrder.First(e => e.OwnerUserId == player.Id).Id;
            var r = _engine.ForceInitiativeRollAsync(_state, _host, playerEntryId);
            Assert.IsTrue(r.IsSuccess);
            var playerEntry = _state.ActiveCombat!.TurnOrder.First(e => e.Id == playerEntryId);
            Assert.IsTrue(playerEntry.IsForceRolled);
            var forced = _state.RollLog.First(rr => rr.RollerUserId == player.Id);
            Assert.AreEqual(_host.Id, forced.ForcedByUserId);
        }

        [TestMethod]
        public void SetNpcInitiative_NonHost_Rejected()
        {
            var npcId = SeedNpcToken();
            _engine.StartInitiativeAsync(_state, _host, [npcId]);
            var npcEntry = _state.ActiveCombat!.TurnOrder.First();

            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.SetNpcInitiativeAsync(_state, player, npcEntry.Id, 15);
            Assert.IsTrue(r.IsFailure);
        }

        // The manual-set verb appends a RollResult so DiceCanvas animates a
        // die that "happens to land" on the host-entered value. Sheetless
        // NPC → modifier 0 → d20 face equals the entered value.
        [TestMethod]
        public void SetNpcInitiative_NoSheet_AppendsForcedRollMatchingEnteredValue()
        {
            var npcId = SeedNpcToken();
            _engine.StartInitiativeAsync(_state, _host, [npcId]);
            var npcEntry = _state.ActiveCombat!.TurnOrder.First();

            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, npcEntry.Id, 15).IsSuccess);

            Assert.AreEqual(15, _state.ActiveCombat!.TurnOrder.First(e => e.Id == npcEntry.Id).InitiativeRoll);
            var logged = _state.RollLog.Single(rr => rr.TokenId == npcId && rr.Label == "Initiative");
            Assert.AreEqual(_host.Id, logged.RollerUserId);
            Assert.AreEqual(_host.Id, logged.ForcedByUserId);
            Assert.AreEqual(15, logged.Total);
            Assert.HasCount(1, logged.Rolls);
            Assert.AreEqual(20, logged.Rolls[0].Sides);
            Assert.AreEqual(15, logged.Rolls[0].Result);
            Assert.AreEqual(0, logged.AttributeModifier);
        }

        // With a positive modifier attached via the NPC's sheet, the d20 face
        // is back-solved so face + modifier == entered.
        [TestMethod]
        public void SetNpcInitiative_WithSheetModifier_BackSolvesD20Face()
        {
            // DEX 14 → +2 (default initiative attribute on the built-in 5e schema).
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "DEX", AttributeValue.Score(14)).IsSuccess);

            var npcId = SeedNpcToken();
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, npcId, sheetId).IsSuccess);
            _engine.StartInitiativeAsync(_state, _host, [npcId]);
            var npcEntry = _state.ActiveCombat!.TurnOrder.First();

            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, npcEntry.Id, 17).IsSuccess);

            var logged = _state.RollLog.Single(rr => rr.TokenId == npcId && rr.Label == "Initiative");
            Assert.AreEqual(15, logged.Rolls[0].Result); // 17 - 2 = 15
            Assert.AreEqual(2, logged.AttributeModifier);
            Assert.AreEqual(17, logged.Total);
            // CombatantEntry keeps the host's entered value.
            Assert.AreEqual(17, _state.ActiveCombat.TurnOrder.First(e => e.Id == npcEntry.Id).InitiativeRoll);
        }

        // Entered value above the reachable face+modifier ceiling clamps the
        // visible die to 20; the CombatantEntry still holds the typed value
        // so the host's override wins in the sorted turn order.
        [TestMethod]
        public void SetNpcInitiative_AboveReachable_ClampsFaceToTwenty_KeepsEnteredOnEntry()
        {
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "DEX", AttributeValue.Score(14)).IsSuccess);
            var npcId = SeedNpcToken();
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, npcId, sheetId).IsSuccess);
            _engine.StartInitiativeAsync(_state, _host, [npcId]);
            var npcEntry = _state.ActiveCombat!.TurnOrder.First();

            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, npcEntry.Id, 30).IsSuccess);

            var logged = _state.RollLog.Single(rr => rr.TokenId == npcId && rr.Label == "Initiative");
            Assert.AreEqual(20, logged.Rolls[0].Result); // clamped to natural-20
            Assert.AreEqual(22, logged.Total);           // 20 + 2 (mod)
            // The host's typed value wins the turn order despite the unreachable maths.
            Assert.AreEqual(30, _state.ActiveCombat!.TurnOrder.First(e => e.Id == npcEntry.Id).InitiativeRoll);
        }

        // Multi-NPC: setting one of two does NOT yet append a RollResult or
        // commit InitiativeRoll — the value parks on PendingInitiative until
        // every NPC has a value, so all the dice can fire together.
        [TestMethod]
        public void SetNpcInitiative_MultipleNpcs_DefersUntilEveryNpcIsSet()
        {
            var npc1 = SeedNpcToken("Orc1");
            var npc2 = SeedNpcToken("Orc2");
            _engine.StartInitiativeAsync(_state, _host, [npc1, npc2]);
            var e1 = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npc1);
            var e2 = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npc2);

            // First Set: value lives on PendingInitiative; no roll logged yet.
            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, e1.Id, 17).IsSuccess);
            var after1 = _state.ActiveCombat!.TurnOrder.First(e => e.Id == e1.Id);
            Assert.IsNull(after1.InitiativeRoll);
            Assert.AreEqual(17, after1.PendingInitiative);
            Assert.IsFalse(_state.RollLog.Any(rr => rr.TokenId == npc1));

            // Second (and last) Set: both pending values commit in one batch,
            // both RollResults appear together so DiceCanvas animates them
            // concurrently.
            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, e2.Id, 13).IsSuccess);
            var committed1 = _state.ActiveCombat!.TurnOrder.First(e => e.Id == e1.Id);
            var committed2 = _state.ActiveCombat!.TurnOrder.First(e => e.Id == e2.Id);
            Assert.AreEqual(17, committed1.InitiativeRoll);
            Assert.IsNull(committed1.PendingInitiative);
            Assert.AreEqual(13, committed2.InitiativeRoll);
            Assert.IsNull(committed2.PendingInitiative);
            Assert.HasCount(2, _state.RollLog.Where(rr => rr.Label == "Initiative").ToList());
        }

        // The host can press Roll All Unset NPCs while some NPCs have pending
        // values from earlier manual Sets. Those pending NPCs commit at their
        // typed values; the truly-unset NPCs roll a real d20.
        [TestMethod]
        public void RollAllNpc_CommitsPendingManualSets_AndRollsTheRest()
        {
            var npc1 = SeedNpcToken("Orc1");
            var npc2 = SeedNpcToken("Orc2");
            _engine.StartInitiativeAsync(_state, _host, [npc1, npc2]);
            var e1 = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npc1);

            // Stage npc1 but leave npc2 unset — npc1's roll deferred.
            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, e1.Id, 17).IsSuccess);
            Assert.IsFalse(_state.RollLog.Any(rr => rr.TokenId == npc1));

            // Bulk roll flushes pending + rolls the rest.
            Assert.IsTrue(_engine.RollAllNpcInitiativeAsync(_state, _host).IsSuccess);
            var n1 = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npc1);
            var n2 = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npc2);
            Assert.AreEqual(17, n1.InitiativeRoll);   // honored host's typed value
            Assert.IsNull(n1.PendingInitiative);
            Assert.AreEqual(10, n2.InitiativeRoll);   // sequential RNG → 10
            Assert.HasCount(2, _state.RollLog.Where(rr => rr.Label == "Initiative").ToList());
        }

        // Symmetric clamp on the low end — entered total below face+modifier
        // floor pins the displayed die to a natural-1.
        [TestMethod]
        public void SetNpcInitiative_BelowReachable_ClampsFaceToOne()
        {
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "DEX", AttributeValue.Score(14)).IsSuccess);
            var npcId = SeedNpcToken();
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, npcId, sheetId).IsSuccess);
            _engine.StartInitiativeAsync(_state, _host, [npcId]);
            var npcEntry = _state.ActiveCombat!.TurnOrder.First();

            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, npcEntry.Id, -5).IsSuccess);

            var logged = _state.RollLog.Single(rr => rr.TokenId == npcId && rr.Label == "Initiative");
            Assert.AreEqual(1, logged.Rolls[0].Result);
            Assert.AreEqual(3, logged.Total); // 1 + 2 mod
            Assert.AreEqual(-5, _state.ActiveCombat!.TurnOrder.First(e => e.Id == npcEntry.Id).InitiativeRoll);
        }

        [TestMethod]
        public void AutoTransition_OnLastRoll_SortsAndActivates()
        {
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            var npcId = SeedNpcToken("Goblin");
            _engine.StartInitiativeAsync(_state, _host, [npcId]);

            var npcEntry = _state.ActiveCombat!.TurnOrder.First(e => e.OwnerUserId is null);

            _engine.SetNpcInitiativeAsync(_state, _host, npcEntry.Id, 20);
            Assert.AreEqual(CombatPhase.WaitingForRolls, _state.ActiveCombat.Phase);

            _engine.SubmitInitiativeRollAsync(_state, player); // d20 returns 10 (sequential RNG)
            Assert.AreEqual(CombatPhase.Active, _state.ActiveCombat.Phase);
            // NPC's 20 outranks player's 10 → NPC first.
            Assert.IsNull(_state.ActiveCombat.TurnOrder[0].OwnerUserId);
        }

        [TestMethod]
        public void AdvanceTurn_WrapsAndIncrementsRound()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            _engine.StartInitiativeAsync(_state, _host, []);
            _engine.SubmitInitiativeRollAsync(_state, player); // auto-transitions

            Assert.AreEqual(0, _state.ActiveCombat!.CurrentTurnIndex);
            Assert.AreEqual(1, _state.ActiveCombat.RoundNumber);

            _engine.AdvanceTurnAsync(_state, _host); // wraps (count=1)
            Assert.AreEqual(0, _state.ActiveCombat.CurrentTurnIndex);
            Assert.AreEqual(2, _state.ActiveCombat.RoundNumber);
        }

        [TestMethod]
        public void AddCombatant_InsertsAtCorrectPosition_AndShiftsCurrentIndex()
        {
            var p1 = EngineTestFactory.RegisterPlayer(_state, "P1");
            _engine.StartInitiativeAsync(_state, _host, []);
            _engine.SubmitInitiativeRollAsync(_state, p1); // active phase, P1's roll = 10

            // Add a higher-rolling NPC mid-combat.
            var npcId = SeedNpcToken();
            var add = _engine.AddCombatantAsync(_state, _host, npcId, 18);
            Assert.IsTrue(add.IsSuccess);

            // Insertion index 0 (NPC outranks). CurrentTurnIndex must shift from 0 → 1.
            Assert.HasCount(2, _state.ActiveCombat!.TurnOrder);
            Assert.AreEqual(p1.Id, _state.ActiveCombat.TurnOrder[1].OwnerUserId);
            Assert.AreEqual(1, _state.ActiveCombat.CurrentTurnIndex);
        }

        [TestMethod]
        public void AddCombatant_DuplicateToken_Rejected()
        {
            var p1 = EngineTestFactory.RegisterPlayer(_state, "P1");
            _engine.StartInitiativeAsync(_state, _host, []);
            _engine.SubmitInitiativeRollAsync(_state, p1);

            var npcId = SeedNpcToken();
            var first = _engine.AddCombatantAsync(_state, _host, npcId, 15);
            Assert.IsTrue(first.IsSuccess);

            var second = _engine.AddCombatantAsync(_state, _host, npcId, 18);
            Assert.IsTrue(second.IsFailure);
            // Turn order still has exactly the original two entries (P1 + first NPC).
            Assert.HasCount(2, _state.ActiveCombat!.TurnOrder);
        }

        [TestMethod]
        public void RemoveCombatant_LastOne_EndsCombat()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            _engine.StartInitiativeAsync(_state, _host, []);
            _engine.SubmitInitiativeRollAsync(_state, player);

            var entry = _state.ActiveCombat!.TurnOrder[0];
            _engine.RemoveCombatantAsync(_state, _host, entry.Id);
            Assert.IsNull(_state.ActiveCombat);
        }

        [TestMethod]
        public void RemoveCombatant_CurrentTurn_AdvancesAutomatically()
        {
            var p1 = EngineTestFactory.RegisterPlayer(_state, "P1");
            var p2 = EngineTestFactory.RegisterPlayer(_state, "P2");
            _engine.StartInitiativeAsync(_state, _host, []);
            _engine.SubmitInitiativeRollAsync(_state, p1);
            _engine.SubmitInitiativeRollAsync(_state, p2);
            Assert.AreEqual(CombatPhase.Active, _state.ActiveCombat!.Phase);

            var current = _state.ActiveCombat.TurnOrder[_state.ActiveCombat.CurrentTurnIndex];
            int beforeCount = _state.ActiveCombat.TurnOrder.Length;
            _engine.RemoveCombatantAsync(_state, _host, current.Id);
            Assert.HasCount(beforeCount - 1, _state.ActiveCombat.TurnOrder);
        }

        [TestMethod]
        public void EndCombat_ClearsActiveCombat()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            _engine.StartInitiativeAsync(_state, _host, []);
            _engine.EndCombatAsync(_state, _host);
            Assert.IsNull(_state.ActiveCombat);
        }
    }
}
