using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    // Covers the three-tier roll template library: built-in (immutable),
    // global (host-only CRUD, save-slot scoped), and per-sheet (owner-or-host
    // CRUD). Also covers the schema-swap behaviour — the stored AttributeName
    // is never mutated, even when the active schema no longer carries it, so
    // restoring the schema later re-binds the modifier without data loss.
    [TestClass]
    public class RollTemplateVerbsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private Guid SeedPlayerSheet(out User player)
        {
            player = EngineTestFactory.RegisterPlayer(_state);
            var sheet = _engine.CreateSheetAsync(_state, player, player.Id, "Char");
            Assert.IsTrue(sheet.TryGetSuccess(out var id));
            return id;
        }

        private static List<DiceTerm> D20() => [new DiceTerm(1, 20)];

        // ── Global tier ─────────────────────────────────────────────────

        [TestMethod]
        public void CreateGlobal_AsHost_Succeeds()
        {
            var result = _engine.CreateGlobalRollTemplateAsync(
                _state, _host, "Initiative", D20(), 0, RollMode.Normal, "DEX", "Initiative");
            Assert.IsTrue(result.TryGetSuccess(out var id));
            Assert.HasCount(1, _state.GlobalRollTemplates);
            Assert.AreEqual(id, _state.GlobalRollTemplates[0].Id);
            Assert.AreEqual("DEX", _state.GlobalRollTemplates[0].AttributeName);
            Assert.AreEqual(RollTemplateScope.Global, _state.GlobalRollTemplates[0].Scope);
        }

        [TestMethod]
        public void CreateGlobal_AsPlayer_Rejects()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.CreateGlobalRollTemplateAsync(
                _state, player, "Initiative", D20(), 0, RollMode.Normal, "DEX", "Initiative");
            Assert.IsTrue(result.IsFailure);
            Assert.IsEmpty(_state.GlobalRollTemplates);
        }

        [TestMethod]
        public void UpdateGlobal_AsPlayer_Rejects()
        {
            Assert.IsTrue(_engine.CreateGlobalRollTemplateAsync(
                _state, _host, "Init", D20(), 0, RollMode.Normal, "DEX", "Init")
                .TryGetSuccess(out var id));

            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.UpdateGlobalRollTemplateAsync(
                _state, player, id, "Pwn", D20(), 99, RollMode.Normal, "STR", "Pwn");
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual("Init", _state.GlobalRollTemplates[0].Name);
        }

        [TestMethod]
        public void DeleteGlobal_AsPlayer_Rejects()
        {
            Assert.IsTrue(_engine.CreateGlobalRollTemplateAsync(
                _state, _host, "Init", D20(), 0, RollMode.Normal, null, "Init")
                .TryGetSuccess(out var id));

            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.DeleteGlobalRollTemplateAsync(_state, player, id);
            Assert.IsTrue(result.IsFailure);
            Assert.HasCount(1, _state.GlobalRollTemplates);
        }

        [TestMethod]
        public void UpdateGlobal_BuiltInId_Rejects()
        {
            var result = _engine.UpdateGlobalRollTemplateAsync(
                _state, _host, DndMapperGameState.BuiltInRollD20Id, "Hacked",
                D20(), 99, RollMode.Normal, null, "Hacked");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void DeleteGlobal_BuiltInId_Rejects()
        {
            var result = _engine.DeleteGlobalRollTemplateAsync(_state, _host, DndMapperGameState.BuiltInRollD20Id);
            Assert.IsTrue(result.IsFailure);
        }

        // ── Per-sheet tier ──────────────────────────────────────────────

        [TestMethod]
        public void CreateSheet_AsOwner_Succeeds()
        {
            var sheetId = SeedPlayerSheet(out var player);
            var result = _engine.CreateSheetRollTemplateAsync(
                _state, player, sheetId, "Spell Attack", D20(), 5, RollMode.Normal, "INT", "Spell Attack");
            Assert.IsTrue(result.TryGetSuccess(out _));
            Assert.HasCount(1, _state.Sheets[sheetId].RollTemplates);
            Assert.AreEqual(RollTemplateScope.Sheet, _state.Sheets[sheetId].RollTemplates[0].Scope);
        }

        [TestMethod]
        public void CreateSheet_AsHost_OnPlayerSheet_Succeeds()
        {
            var sheetId = SeedPlayerSheet(out _);
            var result = _engine.CreateSheetRollTemplateAsync(
                _state, _host, sheetId, "Custom", D20(), 0, RollMode.Normal, null, "Custom");
            Assert.IsTrue(result.IsSuccess);
            Assert.HasCount(1, _state.Sheets[sheetId].RollTemplates);
        }

        [TestMethod]
        public void CreateSheet_AsPlayer_OnOtherSheet_Rejects()
        {
            var sheetId = SeedPlayerSheet(out _);
            var intruder = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.CreateSheetRollTemplateAsync(
                _state, intruder, sheetId, "Custom", D20(), 0, RollMode.Normal, null, "Custom");
            Assert.IsTrue(result.IsFailure);
            Assert.IsEmpty(_state.Sheets[sheetId].RollTemplates);
        }

        [TestMethod]
        public void DeleteSheet_AsOwner_RemovesFromList()
        {
            var sheetId = SeedPlayerSheet(out var player);
            Assert.IsTrue(_engine.CreateSheetRollTemplateAsync(
                _state, player, sheetId, "Custom", D20(), 0, RollMode.Normal, null, "Custom")
                .TryGetSuccess(out var id));

            var del = _engine.DeleteSheetRollTemplateAsync(_state, player, sheetId, id);
            Assert.IsTrue(del.IsSuccess);
            Assert.IsEmpty(_state.Sheets[sheetId].RollTemplates);
        }

        // ── Dice validation ─────────────────────────────────────────────

        [TestMethod]
        public void Create_InvalidDieSize_Rejects()
        {
            var result = _engine.CreateGlobalRollTemplateAsync(
                _state, _host, "Weird", [new DiceTerm(1, 7)], 0, RollMode.Normal, null, "Weird");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void Create_AdvDisWithMultipleDice_Rejects()
        {
            var result = _engine.CreateGlobalRollTemplateAsync(
                _state, _host, "AdvMulti", [new DiceTerm(2, 6)], 0, RollMode.Advantage, null, "AdvMulti");
            Assert.IsTrue(result.IsFailure);
        }

        // ── Schema swap behaviour ───────────────────────────────────────

        [TestMethod]
        public void SchemaSwap_PreservesStoredAttributeName_OnTemplates()
        {
            var sheetId = SeedPlayerSheet(out _);
            Assert.IsTrue(_engine.CreateGlobalRollTemplateAsync(
                _state, _host, "Init", D20(), 0, RollMode.Normal, "DEX", "Init")
                .TryGetSuccess(out _));
            Assert.IsTrue(_engine.CreateSheetRollTemplateAsync(
                _state, _host, sheetId, "Spell", D20(), 5, RollMode.Normal, "INT", "Spell")
                .TryGetSuccess(out _));

            // Swap to Simple d20 — its schema doesn't carry INT/DEX. Stored
            // names must still be present on both the global and sheet
            // templates so flipping back to DnD5e restores them.
            Assert.IsTrue(_engine.ChangeSchemaAsync(_state, _host,
                AttributeSchema.FromPreset(AttributePreset.SimpleD20)).IsSuccess);

            Assert.AreEqual("DEX", _state.GlobalRollTemplates[0].AttributeName);
            Assert.AreEqual("INT", _state.Sheets[sheetId].RollTemplates[0].AttributeName);
        }

        // ── Built-ins ───────────────────────────────────────────────────

        [TestMethod]
        public void BuiltIns_AlwaysPresent_AndImmutableThroughEngine()
        {
            // Build a fresh state with no customs; built-ins are a static
            // property on the type, so they're always available regardless
            // of state lifecycle.
            Assert.IsTrue(DndMapperGameState.BuiltInRollTemplates.Count >= 7);
            Assert.IsTrue(DndMapperGameState.IsBuiltInRollTemplateId(DndMapperGameState.BuiltInRollD20Id));
            Assert.IsTrue(DndMapperGameState.IsBuiltInRollTemplateId(DndMapperGameState.BuiltInRoll2d6Id));
            Assert.IsFalse(DndMapperGameState.IsBuiltInRollTemplateId(Guid.NewGuid()));
        }
    }
}
