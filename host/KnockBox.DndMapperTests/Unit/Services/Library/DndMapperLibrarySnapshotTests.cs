using System.Text.Json;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    [TestClass]
    public class DndMapperLibrarySnapshotTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        // ── FromState ────────────────────────────────────────────────────────────

        [TestMethod]
        public void FromState_CapturesSettingsMapsTokensSheets_DropsRollLog()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Forest").TryGetSuccess(out var mapId));
            Assert.IsTrue(engine.SetActiveMapAsync(state, host, mapId).IsSuccess);
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, mapId, "Goblin").IsSuccess);
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Goblin").IsSuccess);

            var img = new MapImage
            {
                Id = Guid.NewGuid(),
                ContentType = "image/png",
                ShareToken = Guid.NewGuid(),
                Width = 10, Height = 10,
                ByteSize = 1024,
            };
            Assert.IsTrue(engine.AddImageAsync(state, host, mapId, img).IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);

            Assert.AreEqual(1, snap.SchemaVersion);
            Assert.AreEqual(1, snap.Maps.Count);
            Assert.AreEqual("Forest", snap.Maps[0].Name);
            Assert.AreEqual(1, snap.Maps[0].Tokens.Count);
            Assert.AreEqual(1, snap.Maps[0].Images.Count);
            Assert.AreEqual("image/png", snap.Maps[0].Images[0].ContentType);
            Assert.AreEqual(1, snap.Sheets.Count);
            Assert.AreEqual(AttributePreset.DnD5eCore, snap.AttributeSchema.Preset);
        }

        [TestMethod]
        public void FromState_ImageSnapshotDoesNotCarryShareToken()
        {
            // Capability tokens are circuit-scoped — they must never round-trip
            // through IndexedDB; the host republishes on every Attach.
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "M").TryGetSuccess(out var mapId));
            var img = new MapImage
            {
                Id = Guid.NewGuid(),
                ContentType = "image/png",
                ShareToken = Guid.NewGuid(),
                Width = 10, Height = 10,
                ByteSize = 100,
            };
            Assert.IsTrue(engine.AddImageAsync(state, host, mapId, img).IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);

            // The snapshot DTO doesn't even have a ShareToken field — so a JSON
            // roundtrip can't reintroduce one. Serialize + assert absence.
            var json = JsonSerializer.Serialize(snap, JsonOptions);
            Assert.IsFalse(json.Contains("ShareToken", StringComparison.OrdinalIgnoreCase),
                "Snapshot JSON should not contain a ShareToken — it leaked via property naming.");
        }

        // ── JSON round-trip ──────────────────────────────────────────────────────

        [TestMethod]
        public void Snapshot_RoundTripsThroughJson()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Cavern").TryGetSuccess(out var mapId));
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, mapId, "Bat").IsSuccess);
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Bat").IsSuccess);

            var original = LibrarySnapshotMapper.FromState(state);
            var json = JsonSerializer.Serialize(original, JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            Assert.AreEqual(original.SchemaVersion, reread!.SchemaVersion);
            Assert.AreEqual(original.Maps.Count, reread.Maps.Count);
            Assert.AreEqual(original.Maps[0].Name, reread.Maps[0].Name);
            Assert.AreEqual(original.Maps[0].Tokens[0].Name, reread.Maps[0].Tokens[0].Name);
            Assert.AreEqual(original.Sheets.Count, reread.Sheets.Count);
            Assert.AreEqual(original.AttributeSchema.Preset, reread.AttributeSchema.Preset);
        }

        // ── AttributeSchema mapping ──────────────────────────────────────────────

        [TestMethod]
        public void AttributeSchema_BuiltInPreset_RoundTripsViaFromPreset()
        {
            // Built-in presets shouldn't freeze their rows into the snapshot;
            // the mapper rebuilds them from AttributeSchema.FromPreset so any
            // future preset evolution applies to old snapshots.
            var snap = new AttributeSchemaSnapshot { Preset = AttributePreset.DnD5eCore, Rows = [] };
            var schema = LibrarySnapshotMapper.ToAttributeSchema(snap);
            Assert.AreEqual(AttributePreset.DnD5eCore, schema.Preset);
            Assert.IsTrue(schema.Rows.Count > 0, "DnD5eCore preset should have non-empty rows.");
        }

        [TestMethod]
        public void AttributeSchema_CustomPreset_ReplaysPersistedRows()
        {
            var snap = new AttributeSchemaSnapshot
            {
                Preset = AttributePreset.Custom,
                Rows =
                [
                    new AttributeRowSnapshot
                    {
                        Name = "Spice",
                        Type = AttributeValueType.Score,
                        Default = new AttributeValueSnapshot { Type = AttributeValueType.Score, IntValue = 12 },
                    },
                ],
            };

            var schema = LibrarySnapshotMapper.ToAttributeSchema(snap);

            Assert.AreEqual(AttributePreset.Custom, schema.Preset);
            Assert.AreEqual(1, schema.Rows.Count);
            Assert.AreEqual("Spice", schema.Rows[0].Name);
            Assert.AreEqual(12, schema.Rows[0].Default.IntValue);
        }

        [TestMethod]
        public void AttributeValue_TextAndModifier_RoundTripThroughSnapshot()
        {
            var text = new AttributeValueSnapshot { Type = AttributeValueType.Text, StringValue = "Curious" };
            var mod = new AttributeValueSnapshot { Type = AttributeValueType.Modifier, IntValue = 3 };

            var textValue = LibrarySnapshotMapper.ToAttributeValue(text);
            var modValue = LibrarySnapshotMapper.ToAttributeValue(mod);

            Assert.AreEqual(AttributeValueType.Text, textValue.Type);
            Assert.AreEqual("Curious", textValue.StringValue);
            Assert.AreEqual(AttributeValueType.Modifier, modValue.Type);
            Assert.AreEqual(3, modValue.IntValue);
        }
    }
}
