using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class ResetSessionVerbTests
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
        public void ResetSessionAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.ResetSessionAsync(_state, player);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ResetSessionAsync_AlreadyDisposed_ReturnsError()
        {
            Assert.IsTrue(_engine.EndSessionAsync(_state, _host).IsSuccess);
            var result = _engine.ResetSessionAsync(_state, _host);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ResetSessionAsync_HostCaller_ClearsMapsSheetsRollsAndResetsSchemaAndSettings()
        {
            // Build up some state.
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "Forest").TryGetSuccess(out var mapId));
            Assert.IsTrue(_engine.SetActiveMapAsync(_state, _host, mapId).IsSuccess);
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").IsSuccess);
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin").IsSuccess);

            var settings = _state.Settings with { HpTrackingEnabled = !_state.Settings.HpTrackingEnabled };
            Assert.IsTrue(_engine.UpdateSettingsAsync(_state, _host, settings).IsSuccess);

            // Mutate schema away from the DnD5eCore preset so we can observe the reset.
            var customRows = new List<AttributeRow>
            {
                new("STR", AttributeValueType.Score, AttributeValue.Score(12)),
                new("Notes", AttributeValueType.Text, AttributeValue.Text("hi")),
            };
            var saveResult = _engine.CreateCustomTemplateAsync(_state, _host, "MyTpl", customRows);
            Assert.IsTrue(saveResult.TryGetSuccess(out var userTemplateId));
            Assert.IsTrue(_engine.ApplyCustomTemplateAsync(_state, _host, userTemplateId).IsSuccess);

            var reset = _engine.ResetSessionAsync(_state, _host);
            Assert.IsTrue(reset.IsSuccess);

            Assert.IsEmpty(_state.Maps);
            Assert.IsNull(_state.ActiveMapId);
            Assert.IsEmpty(_state.Sheets);
            Assert.IsEmpty(_state.RollLog);
            Assert.AreEqual(0, _state.BytesUsed);
            Assert.AreEqual(new DndMapperSettings().HpTrackingEnabled, _state.Settings.HpTrackingEnabled);
            Assert.AreEqual(AttributePreset.DnD5eCore, _state.AttributeSchema.Preset);
        }

        [TestMethod]
        public void ResetSessionAsync_PreservesBuiltInAndUserTemplates()
        {
            var rows = new List<AttributeRow>
            {
                new("STR", AttributeValueType.Score, AttributeValue.Score(10)),
            };
            Assert.IsTrue(_engine.CreateCustomTemplateAsync(_state, _host, "Keep me", rows).TryGetSuccess(out var userTemplateId));

            var builtInIds = new[]
            {
                DndMapperGameState.BuiltInDnD5eCoreId,
                DndMapperGameState.BuiltInDnD5ePlusSkillsId,
                DndMapperGameState.BuiltInSimpleD20Id,
            };
            foreach (var id in builtInIds) Assert.IsTrue(_state.CustomTemplates.ContainsKey(id));

            Assert.IsTrue(_engine.ResetSessionAsync(_state, _host).IsSuccess);

            foreach (var id in builtInIds)
                Assert.IsTrue(_state.CustomTemplates.ContainsKey(id), $"Built-in {id} should survive reset.");
            Assert.IsTrue(_state.CustomTemplates.ContainsKey(userTemplateId), "User template should survive reset.");
        }
    }
}
