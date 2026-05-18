using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    // Covers the §8.5/v1.x evolution that scopes status-effect templates to
    // the active attribute schema: created under one schema, hidden under
    // others, cascade-deleted when the parent schema is deleted, and the
    // active-schema pointer tracked through the various schema-change paths.
    [TestClass]
    public class StatusEffectSchemaScopingTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        [TestMethod]
        public void FreshState_ActiveSchemaIs_DnD5eCore_BuiltIn()
        {
            Assert.AreEqual(DndMapperGameState.BuiltInDnD5eCoreId, _state.ActiveSchemaTemplateId);
            Assert.IsNotNull(_state.GetActiveSchemaTemplate());
            Assert.IsTrue(_state.GetActiveSchemaTemplate()!.IsBuiltIn);
        }

        [TestMethod]
        public void CreateStatusEffectTemplate_AddsToActiveSchemasList()
        {
            var r = _engine.CreateStatusEffectTemplateAsync(_state, _host, "Frostbite",
                [new AttributeDelta("STR", -2)], null, null, null);
            Assert.IsTrue(r.IsSuccess);

            var active = _state.GetActiveSchemaTemplate()!;
            Assert.HasCount(1, active.StatusEffectTemplates);
            Assert.AreEqual("Frostbite", active.StatusEffectTemplates[0].Name);
        }

        [TestMethod]
        public void CreateStatusEffectTemplate_FreeFormCustomSchema_Rejects()
        {
            // Apply a free-form Custom schema (no source template id) — host
            // must save the schema as a named template before authoring
            // effects under it.
            var custom = new AttributeSchema(AttributePreset.Custom,
                [new AttributeRow("STR", AttributeValueType.Score, AttributeValue.Score(10))]);
            Assert.IsTrue(_engine.ChangeSchemaAsync(_state, _host, custom).IsSuccess);
            Assert.IsNull(_state.ActiveSchemaTemplateId);

            var r = _engine.CreateStatusEffectTemplateAsync(_state, _host, "X",
                [new AttributeDelta("STR", -1)], null, null, null);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void ChangeSchema_ToAnotherBuiltIn_UpdatesActiveAndIsolatesLibraries()
        {
            // Author "Frostbite" under DnD5e core.
            Assert.IsTrue(_engine.CreateStatusEffectTemplateAsync(_state, _host, "Frostbite",
                [new AttributeDelta("STR", -2)], null, null, null).IsSuccess);

            // Switch to Simple d20 — built-in, so active pointer flips, but
            // Frostbite stays under DnD5e core's library.
            Assert.IsTrue(_engine.ChangeSchemaAsync(_state, _host,
                AttributeSchema.FromPreset(AttributePreset.SimpleD20)).IsSuccess);
            Assert.AreEqual(DndMapperGameState.BuiltInSimpleD20Id, _state.ActiveSchemaTemplateId);
            Assert.IsEmpty(_state.GetActiveSchemaTemplate()!.StatusEffectTemplates);

            // The DnD5e core library is intact under its own NamedTemplate.
            var oldSchema = _state.CustomTemplates[DndMapperGameState.BuiltInDnD5eCoreId];
            Assert.HasCount(1, oldSchema.StatusEffectTemplates);
        }

        [TestMethod]
        public void ApplyCustomTemplate_SetsActiveSchemaTemplateIdToThatId()
        {
            IReadOnlyList<AttributeRow> rows =
                [new("STR", AttributeValueType.Score, AttributeValue.Score(10))];
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Homebrew", rows)
                .TryGetSuccess(out var id));

            Assert.IsTrue(_engine.ApplyCustomTemplateAsync(_state, _host, id).IsSuccess);
            Assert.AreEqual(id, _state.ActiveSchemaTemplateId);
            Assert.AreEqual("Homebrew", _state.GetActiveSchemaTemplate()!.Name);
        }

        [TestMethod]
        public void DeleteCustomTemplate_CascadeRemovesItsEffectLibrary()
        {
            IReadOnlyList<AttributeRow> rows =
                [new("STR", AttributeValueType.Score, AttributeValue.Score(10))];
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Homebrew", rows)
                .TryGetSuccess(out var hbId));
            Assert.IsTrue(_engine.ApplyCustomTemplateAsync(_state, _host, hbId).IsSuccess);

            // Author under Homebrew.
            Assert.IsTrue(_engine.CreateStatusEffectTemplateAsync(_state, _host, "Curse",
                [new AttributeDelta("STR", -3)], null, null, null).IsSuccess);
            Assert.HasCount(1, _state.CustomTemplates[hbId].StatusEffectTemplates);

            // Delete Homebrew → effect library gone, active rolls back to DnD5e core.
            Assert.IsTrue(_engine.DeleteCustomTemplateAsync(_state, _host, hbId).IsSuccess);
            Assert.IsFalse(_state.CustomTemplates.ContainsKey(hbId));
            Assert.AreEqual(DndMapperGameState.BuiltInDnD5eCoreId, _state.ActiveSchemaTemplateId);
        }

        [TestMethod]
        public void DeleteCustomTemplate_WhenNotActive_LeavesActivePointerAlone()
        {
            IReadOnlyList<AttributeRow> rows =
                [new("STR", AttributeValueType.Score, AttributeValue.Score(10))];
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Homebrew", rows)
                .TryGetSuccess(out var hbId));
            // Stay on DnD5e core (don't apply Homebrew).
            Assert.AreEqual(DndMapperGameState.BuiltInDnD5eCoreId, _state.ActiveSchemaTemplateId);

            Assert.IsTrue(_engine.DeleteCustomTemplateAsync(_state, _host, hbId).IsSuccess);
            Assert.AreEqual(DndMapperGameState.BuiltInDnD5eCoreId, _state.ActiveSchemaTemplateId);
        }

        [TestMethod]
        public void SaveCustomTemplate_NewSchemaStartsWithEmptyEffectLibrary()
        {
            // Author "Frostbite" under DnD5e core.
            Assert.IsTrue(_engine.CreateStatusEffectTemplateAsync(_state, _host, "Frostbite",
                [new AttributeDelta("STR", -2)], null, null, null).IsSuccess);

            // Save the current rows as a new named template — effects do NOT
            // copy across schemas; the new schema starts empty.
            Assert.IsTrue(_engine.SaveCustomTemplateAsync(_state, _host, "ForkOfCore")
                .TryGetSuccess(out var forkId));
            Assert.IsEmpty(_state.CustomTemplates[forkId].StatusEffectTemplates);
        }
    }
}
