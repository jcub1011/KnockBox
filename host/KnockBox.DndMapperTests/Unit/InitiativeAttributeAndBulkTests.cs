using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    // Covers the per-schema initiative attribute setting and the host's
    // bulk "Roll all NPCs" verb. The setting lives on the active schema's
    // NamedTemplate; the bulk verb consults it via the same resolver that
    // SubmitInitiativeRollAsync / ForceInitiativeRollAsync use.
    [TestClass]
    public class InitiativeAttributeAndBulkTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            // Every d20 returns 10 — deterministic across bulk rolls.
            (_engine, _state, _host, _) = EngineTestFactory.Build(10, 10, 10, 10, 10, 10);
            var m = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(m.TryGetSuccess(out var mapId));
            _engine.SetActiveMapAsync(_state, _host, mapId);
        }

        private Guid SpawnNpc(string name = "Orc")
        {
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, _state.ActiveMapId!.Value, name);
            Assert.IsTrue(spawn.TryGetSuccess(out var id));
            return id;
        }

        // ── Per-schema attribute setting ────────────────────────────────

        [TestMethod]
        public void FreshState_BuiltInDnD5eCore_DefaultsToDex()
        {
            var schema = _state.GetActiveSchemaTemplate();
            Assert.IsNotNull(schema);
            Assert.AreEqual("DEX", schema!.InitiativeAttributeName);
        }

        [TestMethod]
        public void SetInitiativeAttribute_AsHost_PersistsOnActiveTemplate()
        {
            var r = _engine.SetInitiativeAttributeAsync(_state, _host, "WIS");
            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual("WIS", _state.GetActiveSchemaTemplate()!.InitiativeAttributeName);
        }

        [TestMethod]
        public void SetInitiativeAttribute_AsPlayer_Rejects()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.SetInitiativeAttributeAsync(_state, player, "WIS");
            Assert.IsTrue(r.IsFailure);
            Assert.AreEqual("DEX", _state.GetActiveSchemaTemplate()!.InitiativeAttributeName);
        }

        [TestMethod]
        public void SetInitiativeAttribute_UnknownAttribute_Rejects()
        {
            var r = _engine.SetInitiativeAttributeAsync(_state, _host, "BOGUS");
            Assert.IsTrue(r.IsFailure);
            Assert.AreEqual("DEX", _state.GetActiveSchemaTemplate()!.InitiativeAttributeName);
        }

        [TestMethod]
        public void SetInitiativeAttribute_FreeFormCustomSchema_Accepts()
        {
            // Apply a free-form Custom schema (no source template id). Even with
            // no named template, the host can configure the initiative attribute;
            // the choice lives on state and round-trips through the snapshot.
            var custom = new AttributeSchema(AttributePreset.Custom,
                [new AttributeRow("STR", AttributeValueType.Score, AttributeValue.Score(10))]);
            Assert.IsTrue(_engine.ChangeSchemaAsync(_state, _host, custom).IsSuccess);
            Assert.IsNull(_state.ActiveSchemaTemplateId);

            var r = _engine.SetInitiativeAttributeAsync(_state, _host, "STR");
            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual("STR", _state.InitiativeAttributeName);
        }

        [TestMethod]
        public void SetInitiativeAttribute_NullClearsField()
        {
            Assert.IsTrue(_engine.SetInitiativeAttributeAsync(_state, _host, null).IsSuccess);
            Assert.IsNull(_state.GetActiveSchemaTemplate()!.InitiativeAttributeName);
        }

        // ── Resolver / submit path uses the configured attribute ────────

        [TestMethod]
        public void SubmitInitiativeRoll_UsesConfiguredAttribute_NotDexFallback()
        {
            // Configure schema to use WIS, give the player WIS=14 (mod +2) and
            // a DEX of 18 (mod +4). If the resolver still used DEX, the player's
            // total would be 14; we want 12.
            Assert.IsTrue(_engine.SetInitiativeAttributeAsync(_state, _host, "WIS").IsSuccess);
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.CreateSheetAsync(_state, player, player.Id, "Alice")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "WIS", AttributeValue.Score(14)).IsSuccess);
            Assert.IsTrue(_engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "DEX", AttributeValue.Score(18)).IsSuccess);

            _engine.StartInitiativeAsync(_state, _host, []);
            Assert.IsTrue(_engine.SubmitInitiativeRollAsync(_state, player).IsSuccess);

            var entry = _state.ActiveCombat!.TurnOrder.First(e => e.OwnerUserId == player.Id);
            Assert.AreEqual(12, entry.InitiativeRoll); // d20=10 + WIS mod +2
        }

        // ── Roll-all-NPCs verb ──────────────────────────────────────────

        [TestMethod]
        public void RollAllNpcs_AsPlayer_Rejects()
        {
            var npcId = SpawnNpc();
            _engine.StartInitiativeAsync(_state, _host, [npcId]);

            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.RollAllNpcInitiativeAsync(_state, player);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void RollAllNpcs_RollsEveryUnrolledNpc()
        {
            var npc1 = SpawnNpc("Goblin1");
            var npc2 = SpawnNpc("Goblin2");
            _engine.StartInitiativeAsync(_state, _host, [npc1, npc2]);

            var r = _engine.RollAllNpcInitiativeAsync(_state, _host);
            Assert.IsTrue(r.IsSuccess);

            // Both NPCs got rolled (d20=10, no sheet → mod 0).
            foreach (var entry in _state.ActiveCombat!.TurnOrder)
            {
                Assert.IsNotNull(entry.InitiativeRoll);
                Assert.AreEqual(10, entry.InitiativeRoll);
            }
            // Two initiative entries appended to the log, both forced by host.
            var initiativeRolls = _state.RollLog.Where(rr => rr.Label == "Initiative").ToList();
            Assert.HasCount(2, initiativeRolls);
            Assert.IsTrue(initiativeRolls.All(rr => rr.ForcedByUserId == _host.Id));
        }

        [TestMethod]
        public void RollAllNpcs_SkipsAlreadyRolledNpcs()
        {
            var npc1 = SpawnNpc("Goblin1");
            var npc2 = SpawnNpc("Goblin2");
            _engine.StartInitiativeAsync(_state, _host, [npc1, npc2]);

            var npc1Entry = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npc1);
            Assert.IsTrue(_engine.SetNpcInitiativeAsync(_state, _host, npc1Entry.Id, 99).IsSuccess);

            Assert.IsTrue(_engine.RollAllNpcInitiativeAsync(_state, _host).IsSuccess);

            // npc1 keeps its manual 99; npc2 gets the d20+0 = 10.
            var n1 = _state.ActiveCombat.TurnOrder.First(e => e.TokenId == npc1);
            var n2 = _state.ActiveCombat.TurnOrder.First(e => e.TokenId == npc2);
            Assert.AreEqual(99, n1.InitiativeRoll);
            Assert.AreEqual(10, n2.InitiativeRoll);
        }

        [TestMethod]
        public void RollAllNpcs_UsesAttachedSheetAttribute()
        {
            // NPC sheet with DEX 14 (mod +2). Schema's initiative attribute
            // defaults to DEX. The bulk roll should pick up the +2 modifier.
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "DEX", AttributeValue.Score(14)).IsSuccess);

            var npcId = SpawnNpc("Goblin");
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, npcId, sheetId).IsSuccess);

            _engine.StartInitiativeAsync(_state, _host, [npcId]);
            Assert.IsTrue(_engine.RollAllNpcInitiativeAsync(_state, _host).IsSuccess);

            var entry = _state.ActiveCombat!.TurnOrder.First(e => e.TokenId == npcId);
            Assert.AreEqual(12, entry.InitiativeRoll); // d20=10 + DEX mod +2
        }

        [TestMethod]
        public void RollAllNpcs_DoesNotRollPlayers()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var npcId = SpawnNpc("Goblin");
            _engine.StartInitiativeAsync(_state, _host, [npcId]);

            Assert.IsTrue(_engine.RollAllNpcInitiativeAsync(_state, _host).IsSuccess);

            var playerEntry = _state.ActiveCombat!.TurnOrder.First(e => e.OwnerUserId == player.Id);
            Assert.IsNull(playerEntry.InitiativeRoll);
            var npcEntry = _state.ActiveCombat.TurnOrder.First(e => e.TokenId == npcId);
            Assert.AreEqual(10, npcEntry.InitiativeRoll);
        }

        [TestMethod]
        public void RollAllNpcs_AllUnrolled_AndOnlyNpcsLeft_AutoTransitionsToActive()
        {
            var npc1 = SpawnNpc("Goblin1");
            var npc2 = SpawnNpc("Goblin2");
            _engine.StartInitiativeAsync(_state, _host, [npc1, npc2]);

            Assert.IsTrue(_engine.RollAllNpcInitiativeAsync(_state, _host).IsSuccess);
            Assert.AreEqual(CombatPhase.Active, _state.ActiveCombat!.Phase);
        }

        // ── Persistence (V3 round-trip) ─────────────────────────────────

        [TestMethod]
        public void InitiativeAttribute_RoundTripsThroughSnapshot()
        {
            Assert.IsTrue(_engine.SetInitiativeAttributeAsync(_state, _host, "WIS").IsSuccess);

            var snap = KnockBox.DndMapper.Services.Library.LibrarySnapshotMapper.FromState(_state);
            var core = snap.CustomTemplates.Single(t => t.Id == DndMapperGameState.BuiltInDnD5eCoreId);
            Assert.AreEqual("WIS", core.InitiativeAttributeName);
        }

        [TestMethod]
        public void InitiativeAttribute_RoundTripsThroughSnapshot_UnderUserCustomSchema()
        {
            // User-saved Custom schemas take a different load-path branch from
            // built-ins (the snapshot value is written verbatim). Pin that
            // InitiativeAttributeName persists on a non-built-in template.
            IReadOnlyList<AttributeRow> rows =
            [
                new("STR", AttributeValueType.Score, AttributeValue.Score(10)),
                new("DEX", AttributeValueType.Score, AttributeValue.Score(10)),
            ];
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Homebrew", rows)
                .TryGetSuccess(out var hbId));
            Assert.IsTrue(_engine.ApplyCustomTemplateAsync(_state, _host, hbId).IsSuccess);
            Assert.IsTrue(_engine.SetInitiativeAttributeAsync(_state, _host, "STR").IsSuccess);

            var snap = KnockBox.DndMapper.Services.Library.LibrarySnapshotMapper.FromState(_state);
            var hb = snap.CustomTemplates.Single(t => t.Id == hbId);
            Assert.IsFalse(hb.IsBuiltIn);
            Assert.AreEqual("STR", hb.InitiativeAttributeName);
        }
    }
}
