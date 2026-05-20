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

            Assert.AreEqual(3, snap.SchemaVersion);
            Assert.HasCount(1, snap.Maps);
            Assert.AreEqual("Forest", snap.Maps[0].Name);
            Assert.HasCount(1, snap.Maps[0].Tokens);
            Assert.HasCount(1, snap.Maps[0].Images);
            Assert.AreEqual("image/png", snap.Maps[0].Images[0].ContentType);
            Assert.HasCount(1, snap.Sheets);
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
            Assert.HasCount(original.Maps.Count, reread.Maps);
            Assert.AreEqual(original.Maps[0].Name, reread.Maps[0].Name);
            Assert.AreEqual(original.Maps[0].Tokens[0].Name, reread.Maps[0].Tokens[0].Name);
            Assert.HasCount(original.Sheets.Count, reread.Sheets);
            Assert.AreEqual(original.AttributeSchema.Preset, reread.AttributeSchema.Preset);
        }

        [TestMethod]
        public void Snapshot_PreservesTokenColorNameAndIconKind()
        {
            // Token.Color, Name, and IconKind are part of the persistence contract:
            // a name-derived default color (set at spawn time), a renamed NPC, and
            // an icon-kind change must all survive a JSON round-trip.
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Cavern").TryGetSuccess(out var mapId));
            Assert.IsTrue(engine.SetActiveMapAsync(state, host, mapId).IsSuccess);

            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, mapId, "Goblin").TryGetSuccess(out var goblinId));
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, mapId, "Bandit").TryGetSuccess(out var banditId));
            // Host renames + recolors one NPC; the other keeps its FromName default.
            Assert.IsTrue(engine.UpdateTokenAsync(state, host, banditId, "Cutpurse", "#abcdef", TokenIconKind.Solid).IsSuccess);

            var json = JsonSerializer.Serialize(LibrarySnapshotMapper.FromState(state), JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            var tokens = reread!.Maps.Single().Tokens.ToDictionary(t => t.Id);
            Assert.AreEqual("Goblin", tokens[goblinId].Name);
            Assert.AreEqual(DefaultColorPalette.FromName("Goblin"), tokens[goblinId].Color);
            Assert.AreEqual(TokenIconKind.Initial, tokens[goblinId].IconKind);
            Assert.AreEqual("Cutpurse", tokens[banditId].Name);
            Assert.AreEqual("#abcdef", tokens[banditId].Color);
            Assert.AreEqual(TokenIconKind.Solid, tokens[banditId].IconKind);
        }

        [TestMethod]
        public void Snapshot_PreservesSheetCharacterNameNotesHpAndAttributeValues()
        {
            // CharacterName, Notes, Hp/MaxHp, and Values are the user-visible payload
            // of a sheet and must all survive a JSON round-trip.
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Bat")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(engine.UpdateSheetFreeFieldsAsync(state, host, sheetId,
                characterName: "Vampire Bat", notes: "Hangs from ceiling", hp: 4, maxHp: 7).IsSuccess);
            var firstAttr = state.AttributeSchema.Rows[0].Name;
            Assert.IsTrue(engine.UpdateSheetAttributeAsync(state, host, sheetId, firstAttr,
                AttributeValue.Score(18)).IsSuccess);

            var json = JsonSerializer.Serialize(LibrarySnapshotMapper.FromState(state), JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            var sheet = reread!.Sheets.Single(s => s.Id == sheetId);
            Assert.AreEqual("Vampire Bat", sheet.CharacterName);
            Assert.AreEqual("Hangs from ceiling", sheet.Notes);
            Assert.AreEqual(4, sheet.Hp);
            Assert.AreEqual(7, sheet.MaxHp);
            Assert.IsTrue(sheet.Values.TryGetValue(firstAttr, out var v));
            Assert.AreEqual(AttributeValueType.Score, v!.Type);
            Assert.AreEqual(18, v.IntValue);
        }

        [TestMethod]
        public void Snapshot_PlayerColorChange_PersistsThroughRoundTrip()
        {
            // A player using MyTokenPanel changes their own token's color via
            // UpdateTokenAsync; the new color must round-trip just like a host pick.
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "M").TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(state, "Alice");
            Assert.IsTrue(engine.SetActiveMapAsync(state, host, mapId).IsSuccess);
            var pToken = state.Maps.Single().Tokens.Single(t => t.OwnerUserId == player.Id);

            Assert.IsTrue(engine.UpdateTokenAsync(state, player, pToken.Id, pToken.Name, "#123456", pToken.IconKind).IsSuccess);

            var json = JsonSerializer.Serialize(LibrarySnapshotMapper.FromState(state), JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            var roundTripped = reread!.Maps.Single().Tokens.Single(t => t.Id == pToken.Id);
            Assert.AreEqual("#123456", roundTripped.Color);
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
            Assert.IsNotEmpty(schema.Rows, "DnD5eCore preset should have non-empty rows.");
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
            Assert.HasCount(1, schema.Rows);
            Assert.AreEqual("Spice", schema.Rows[0].Name);
            Assert.AreEqual(12, schema.Rows[0].Default.IntValue);
        }

        // ── Built-ins are serialized so their status-effect templates can ride along ─

        [TestMethod]
        public void FromState_IncludesBuiltInTemplates_WithIsBuiltInFlag()
        {
            // Built-ins must round-trip because hosts can author status-effect
            // templates underneath them. The Rows on built-ins are still
            // re-seeded from the preset on load (snapshot Rows are ignored
            // for IsBuiltIn entries), so preset evolution remains safe.
            var (engine, state, host, _) = EngineTestFactory.Build();
            IReadOnlyList<AttributeRow> rows =
                [new("STR", AttributeValueType.Score, AttributeValue.Score(10))];
            Assert.IsTrue(engine.CreateCustomTemplateAsync(state, host, "MyTpl", rows)
                .TryGetSuccess(out var userTplId));

            // Sanity: state contains 3 built-ins + 1 user template.
            Assert.HasCount(4, state.CustomTemplates);

            var snap = LibrarySnapshotMapper.FromState(state);

            Assert.HasCount(4, snap.CustomTemplates);
            var user = snap.CustomTemplates.Single(t => t.Id == userTplId);
            Assert.AreEqual("MyTpl", user.Name);
            Assert.IsFalse(user.IsBuiltIn);
            Assert.AreEqual(3, snap.CustomTemplates.Count(t => t.IsBuiltIn));
        }

        // ── Status-effect persistence (V2) ───────────────────────────────────────

        [TestMethod]
        public void FromState_PersistsStatusEffectTemplates_UnderBuiltInSchema()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateStatusEffectTemplateAsync(state, host, "Frostbite",
                [new AttributeDelta("STR", -2)], null, null, "icy").IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);

            var core = snap.CustomTemplates.Single(t => t.Id == KnockBox.DndMapper.Services.State.Games.DndMapperGameState.BuiltInDnD5eCoreId);
            Assert.HasCount(1, core.StatusEffectTemplates);
            Assert.AreEqual("Frostbite", core.StatusEffectTemplates[0].Name);
            Assert.AreEqual(-2, core.StatusEffectTemplates[0].AttributeDeltas[0].Delta);
            Assert.AreEqual("STR", core.StatusEffectTemplates[0].AttributeDeltas[0].AttributeName);
            Assert.AreEqual("icy", core.StatusEffectTemplates[0].Notes);
        }

        [TestMethod]
        public void FromState_PersistsActiveSchemaTemplateId()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var snap = LibrarySnapshotMapper.FromState(state);
            Assert.AreEqual(KnockBox.DndMapper.Services.State.Games.DndMapperGameState.BuiltInDnD5eCoreId,
                snap.ActiveSchemaTemplateId);
        }

        [TestMethod]
        public void FromState_PersistsAppliedStatusEffectsOnSheets()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(engine.ApplyStatusEffectAsync(state, host, sheetId, "Bleed",
                [new AttributeDelta("STR", -1)], maxHpDelta: null, onApplyHpDelta: -3, notes: "bleeding").IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);

            var sheetSnap = snap.Sheets.Single(s => s.Id == sheetId);
            Assert.HasCount(1, sheetSnap.StatusEffects);
            Assert.AreEqual("Bleed", sheetSnap.StatusEffects[0].Name);
            Assert.AreEqual(-3, sheetSnap.StatusEffects[0].OnApplyHpDelta);
            Assert.AreEqual("bleeding", sheetSnap.StatusEffects[0].Notes);
        }

        [TestMethod]
        public void Snapshot_RoundTripsStatusEffects_ThroughJson()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateStatusEffectTemplateAsync(state, host, "Frostbite",
                [new AttributeDelta("STR", -2)], null, null, "icy").IsSuccess);
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(engine.ApplyStatusEffectAsync(state, host, sheetId, "Bleed",
                [new AttributeDelta("STR", -1)], null, null, null).IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);
            var json = JsonSerializer.Serialize(snap, JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            var core = reread!.CustomTemplates.Single(t => t.IsBuiltIn
                && t.Id == KnockBox.DndMapper.Services.State.Games.DndMapperGameState.BuiltInDnD5eCoreId);
            Assert.HasCount(1, core.StatusEffectTemplates);
            Assert.AreEqual("Frostbite", core.StatusEffectTemplates[0].Name);

            var sheet = reread.Sheets.Single(s => s.Id == sheetId);
            Assert.HasCount(1, sheet.StatusEffects);
            Assert.AreEqual("Bleed", sheet.StatusEffects[0].Name);
            Assert.AreEqual(3, reread.SchemaVersion);
        }

        // ── Roll-template persistence (V3) ───────────────────────────────────────

        [TestMethod]
        public void FromState_PersistsGlobalRollTemplates()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateGlobalRollTemplateAsync(state, host, "Initiative",
                [new DiceTerm(1, 20)], 0, RollMode.Normal, "DEX", "Initiative").IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);

            Assert.HasCount(1, snap.GlobalRollTemplates);
            Assert.AreEqual("Initiative", snap.GlobalRollTemplates[0].Name);
            Assert.AreEqual("DEX", snap.GlobalRollTemplates[0].AttributeName);
            Assert.HasCount(1, snap.GlobalRollTemplates[0].Dice);
            Assert.AreEqual(20, snap.GlobalRollTemplates[0].Dice[0].Sides);
        }

        [TestMethod]
        public void FromState_PersistsSheetRollTemplates()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Goblin")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(engine.CreateSheetRollTemplateAsync(state, host, sheetId, "Spell",
                [new DiceTerm(1, 20)], 5, RollMode.Normal, "INT", "Spell").IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);
            var sheetSnap = snap.Sheets.Single(s => s.Id == sheetId);

            Assert.HasCount(1, sheetSnap.RollTemplates);
            Assert.AreEqual("Spell", sheetSnap.RollTemplates[0].Name);
            Assert.AreEqual(5, sheetSnap.RollTemplates[0].FlatModifier);
            Assert.AreEqual("INT", sheetSnap.RollTemplates[0].AttributeName);
        }

        [TestMethod]
        public void Snapshot_RollTemplates_RoundTripThroughJson()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateGlobalRollTemplateAsync(state, host, "Init",
                [new DiceTerm(1, 20)], 0, RollMode.Normal, "DEX", "Init").IsSuccess);
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "G")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(engine.CreateSheetRollTemplateAsync(state, host, sheetId, "Atk",
                [new DiceTerm(1, 20)], 5, RollMode.Advantage, "STR", "Atk").IsSuccess);

            var json = JsonSerializer.Serialize(LibrarySnapshotMapper.FromState(state), JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            Assert.HasCount(1, reread!.GlobalRollTemplates);
            Assert.AreEqual("DEX", reread.GlobalRollTemplates[0].AttributeName);
            var sheet = reread.Sheets.Single(s => s.Id == sheetId);
            Assert.HasCount(1, sheet.RollTemplates);
            Assert.AreEqual(RollMode.Advantage, sheet.RollTemplates[0].Mode);
            Assert.AreEqual(5, sheet.RollTemplates[0].FlatModifier);
        }

        [TestMethod]
        public void FromRollTemplateSnapshot_AssignsScopeFromLocation()
        {
            var snap = new RollTemplateSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "x",
                Dice = [new DiceTermSnapshot { Count = 1, Sides = 20 }],
                FlatModifier = 0,
                Mode = RollMode.Normal,
                AttributeName = null,
                Label = "x",
            };

            var asGlobal = LibrarySnapshotMapper.FromRollTemplateSnapshot(snap, RollTemplateScope.Global);
            var asSheet = LibrarySnapshotMapper.FromRollTemplateSnapshot(snap, RollTemplateScope.Sheet);

            Assert.AreEqual(RollTemplateScope.Global, asGlobal.Scope);
            Assert.AreEqual(RollTemplateScope.Sheet, asSheet.Scope);
        }

        // ── V1 backward compatibility ────────────────────────────────────────────

        [TestMethod]
        public void Snapshot_LoadsV1Json_WithoutNewFields_DeserializesWithSensibleDefaults()
        {
            // Hand-rolled V1 shape: SchemaVersion = 1, no ActiveSchemaTemplateId,
            // no GlobalRollTemplates, NamedTemplate entries lack IsBuiltIn /
            // StatusEffectTemplates / InitiativeAttributeName, SheetSnapshot
            // lacks StatusEffects / RollTemplates. Built-in templates were
            // filtered out before serialization in V1, so CustomTemplates
            // carries only user-saved entries. Proves the DTO defaults keep
            // older disk slots loadable.
            const string v1Json = """
            {
              "SchemaVersion": 1,
              "Settings": {},
              "AttributeSchema": { "Preset": 0, "Rows": [] },
              "Maps": [],
              "Sheets": [
                {
                  "Id": "11111111-1111-1111-1111-111111111111",
                  "CharacterName": "Goblin",
                  "Values": {},
                  "Notes": "",
                  "Hp": null,
                  "MaxHp": null
                }
              ],
              "CustomTemplates": [
                {
                  "Id": "22222222-2222-2222-2222-222222222222",
                  "Name": "MyTpl",
                  "Rows": []
                }
              ]
            }
            """;

            var snap = JsonSerializer.Deserialize<LibrarySnapshot>(v1Json, JsonOptions);

            Assert.IsNotNull(snap);
            Assert.AreEqual(1, snap!.SchemaVersion);
            Assert.IsNull(snap.ActiveSchemaTemplateId);
            Assert.IsEmpty(snap.GlobalRollTemplates);

            Assert.HasCount(1, snap.Sheets);
            Assert.IsEmpty(snap.Sheets[0].StatusEffects);
            Assert.IsEmpty(snap.Sheets[0].RollTemplates);

            Assert.HasCount(1, snap.CustomTemplates);
            var tpl = snap.CustomTemplates[0];
            Assert.IsFalse(tpl.IsBuiltIn);
            Assert.IsEmpty(tpl.StatusEffectTemplates);
            Assert.IsNull(tpl.InitiativeAttributeName);

            // The fallback the load path relies on for V1's missing
            // ActiveSchemaTemplateId: infer from the persisted preset.
            Assert.AreEqual(
                KnockBox.DndMapper.Services.State.Games.DndMapperGameState.BuiltInDnD5eCoreId,
                KnockBox.DndMapper.Services.State.Games.DndMapperGameState.BuiltInTemplateIdFor(
                    snap.AttributeSchema.Preset));
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

        // ── Fog mask ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void FromState_CapturesFogMask()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Crypt").TryGetSuccess(out var mapId));
            Assert.IsTrue(engine.PaintFogAsync(state, host, mapId, new[] { (0, 0), (1, 0), (0, 1) }, fogged: true).IsSuccess);

            var snap = LibrarySnapshotMapper.FromState(state);

            var mapSnap = snap.Maps.Single(m => m.Id == mapId);
            Assert.IsTrue(mapSnap.FogMask.Length > 0);
            Assert.IsTrue(state.Maps.Single(m => m.Id == mapId).IsFogged(0, 0));
        }

        [TestMethod]
        public void Snapshot_FogMask_RoundTripsThroughJsonAsBase64()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            Assert.IsTrue(engine.CreateMapAsync(state, host, "M").TryGetSuccess(out var mapId));
            Assert.IsTrue(engine.FillMapWithFogAsync(state, host, mapId).IsSuccess);

            var original = LibrarySnapshotMapper.FromState(state);
            var json = JsonSerializer.Serialize(original, JsonOptions);
            var reread = JsonSerializer.Deserialize<LibrarySnapshot>(json, JsonOptions);

            Assert.IsNotNull(reread);
            var origMap = original.Maps.Single(m => m.Id == mapId);
            var rereadMap = reread!.Maps.Single(m => m.Id == mapId);
            CollectionAssert.AreEqual(origMap.FogMask, rereadMap.FogMask);
        }

        [TestMethod]
        public void LegacySnapshot_WithoutFogMask_DeserializesToEmpty()
        {
            // Older library payloads predating fog of war don't write a FogMask
            // property. The init-only default ([]) must take effect on deserialize.
            const string legacyJson = """
                {
                    "SchemaVersion": 3,
                    "Maps": [
                        {
                            "Id": "00000000-0000-0000-0000-000000000001",
                            "Name": "Legacy",
                            "ListOrder": 0,
                            "CreatedUtc": "2025-01-01T00:00:00Z",
                            "Grid": { "WidthCells": 10, "HeightCells": 10 },
                            "Images": [],
                            "Tokens": []
                        }
                    ]
                }
                """;

            var snap = JsonSerializer.Deserialize<LibrarySnapshot>(legacyJson, JsonOptions);

            Assert.IsNotNull(snap);
            var mapSnap = snap!.Maps.Single();
            Assert.IsNotNull(mapSnap.FogMask);
            Assert.IsEmpty(mapSnap.FogMask);
        }
    }
}
