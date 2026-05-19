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

            var playerEntry = _state.ActiveCombat!.TurnOrder.First(e => e.OwnerUserId == player.Id);
            var r = _engine.ForceInitiativeRollAsync(_state, _host, playerEntry.Id);
            Assert.IsTrue(r.IsSuccess);
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
            int beforeCount = _state.ActiveCombat.TurnOrder.Count;
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
