using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class AttributeTemplateVerbsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private static IReadOnlyList<AttributeRow> SimpleRows() =>
            [new AttributeRow("STR", AttributeValueType.Score, AttributeValue.Score(10))];

        // ── CreateCustomTemplate ────────────────────────────────────────────────

        [TestMethod]
        public void CreateCustomTemplate_RejectsEmptyName()
        {
            var r = _engine.CreateCustomTemplateAsync(_state, _host, "   ", SimpleRows());
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void CreateCustomTemplate_RejectsEmptyRows()
        {
            var r = _engine.CreateCustomTemplateAsync(_state, _host, "T", []);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void CreateCustomTemplate_RejectsDuplicateRowName_CaseInsensitive()
        {
            IReadOnlyList<AttributeRow> rows =
            [
                new("STR", AttributeValueType.Score, AttributeValue.Score(10)),
                new("str", AttributeValueType.Score, AttributeValue.Score(8)),
            ];
            var r = _engine.CreateCustomTemplateAsync(_state, _host, "T", rows);
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void CreateCustomTemplate_RejectsDuplicateTemplateName_CaseInsensitive()
        {
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Hero", SimpleRows()).TryGetSuccess(out _));
            var r = _engine.CreateCustomTemplateAsync(_state, _host, "hero", SimpleRows());
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void CreateCustomTemplate_NonHost_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.CreateCustomTemplateAsync(_state, player, "Hero", SimpleRows());
            Assert.IsTrue(r.IsFailure);
        }

        // ── Built-in protection ─────────────────────────────────────────────────

        [TestMethod]
        public void UpdateCustomTemplate_OnBuiltIn_ReturnsError()
        {
            var r = _engine.UpdateCustomTemplateAsync(
                _state, _host, DndMapperGameState.BuiltInDnD5eCoreId, SimpleRows());
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void DeleteCustomTemplate_OnBuiltIn_ReturnsError()
        {
            var r = _engine.DeleteCustomTemplateAsync(
                _state, _host, DndMapperGameState.BuiltInDnD5eCoreId);
            Assert.IsTrue(r.IsFailure);
            Assert.IsTrue(_state.CustomTemplates.ContainsKey(DndMapperGameState.BuiltInDnD5eCoreId));
        }

        [TestMethod]
        public void RenameCustomTemplate_OnBuiltIn_ReturnsError()
        {
            var r = _engine.RenameCustomTemplateAsync(
                _state, _host, DndMapperGameState.BuiltInDnD5eCoreId, "Renamed");
            Assert.IsTrue(r.IsFailure);
        }

        // ── RenameCustomTemplate ────────────────────────────────────────────────

        [TestMethod]
        public void RenameCustomTemplate_CollisionWithOtherName_ReturnsError()
        {
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Alpha", SimpleRows()).TryGetSuccess(out var aId));
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Beta", SimpleRows()).TryGetSuccess(out _));
            var r = _engine.RenameCustomTemplateAsync(_state, _host, aId, "Beta");
            Assert.IsTrue(r.IsFailure);
        }

        [TestMethod]
        public void RenameCustomTemplate_SameNameDifferentCasing_AllowedOnSelf()
        {
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Alpha", SimpleRows()).TryGetSuccess(out var aId));
            // Renaming the same template to a case-variant of its own name should succeed
            // because the collision check excludes self.
            var r = _engine.RenameCustomTemplateAsync(_state, _host, aId, "alpha");
            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual("alpha", _state.CustomTemplates[aId].Name);
        }

        [TestMethod]
        public void RenameCustomTemplate_EmptyName_ReturnsError()
        {
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Alpha", SimpleRows()).TryGetSuccess(out var aId));
            var r = _engine.RenameCustomTemplateAsync(_state, _host, aId, "   ");
            Assert.IsTrue(r.IsFailure);
        }
    }
}
