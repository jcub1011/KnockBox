using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class SheetVerbsTests
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
        public void CreateSheetAsync_HostCaller_SeedsValuesFromSchema()
        {
            var result = _engine.CreateSheetAsync(_state, _host, ownerUserId: null, "NPC");
            Assert.IsTrue(result.TryGetSuccess(out var sheetId));
            var sheet = _state.Sheets[sheetId];
            // DnD5eCore: STR/DEX/CON/INT/WIS/CHA = 10
            foreach (var name in new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" })
            {
                Assert.IsTrue(sheet.Values.ContainsKey(name));
                Assert.AreEqual(AttributeValueType.Score, sheet.Values[name].Type);
                Assert.AreEqual(10, sheet.Values[name].IntValue);
            }
        }

        [TestMethod]
        public void CreateSheetAsync_NonHostCreatingNpcSheet_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            // Non-host caller passing ownerUserId=null (NPC sheet) is still rejected.
            var result = _engine.CreateSheetAsync(_state, player, null, "X");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void CreateSheetAsync_PlayerCreatesOwnSheet_Succeeds()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.CreateSheetAsync(_state, player, ownerUserId: player.Id, "Sir Robin");
            Assert.IsTrue(result.TryGetSuccess(out var newId));
            Assert.AreEqual(player.Id, _state.Sheets[newId].OwnerUserId);
        }

        [TestMethod]
        public void CreateSheetAsync_PlayerCreatesSecondOwnSheet_Fails()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            Assert.IsTrue(_engine.CreateSheetAsync(_state, player, player.Id, "First").IsSuccess);

            var second = _engine.CreateSheetAsync(_state, player, player.Id, "Second");
            Assert.IsTrue(second.IsFailure);
        }

        [TestMethod]
        public void CreateSheetAsync_PlayerCreatesSheetForOtherPlayer_Fails()
        {
            var p1 = EngineTestFactory.RegisterPlayer(_state);
            var p2 = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.CreateSheetAsync(_state, p1, ownerUserId: p2.Id, "Stolen identity");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void CreateSheetAsync_EmptyName_ReturnsError()
        {
            var result = _engine.CreateSheetAsync(_state, _host, null, "  ");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetAttributeAsync_HostOnly_OwnerCannotEditOwnSheet()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var sheetId = SeedSheetForPlayer(player.Id);
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { SheetEditByOthers = SheetEditPolicy.HostOnly });

            var update = _engine.UpdateSheetAttributeAsync(_state, player, sheetId, "STR", AttributeValue.Score(16));
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetAttributeAsync_HostOnly_NonOwnerNonHostCannotEdit()
        {
            var owner = EngineTestFactory.RegisterPlayer(_state, "Owner");
            var other = EngineTestFactory.RegisterPlayer(_state, "Other");
            var sheetId = SeedSheetForPlayer(owner.Id);
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { SheetEditByOthers = SheetEditPolicy.HostOnly });

            var update = _engine.UpdateSheetAttributeAsync(_state, other, sheetId, "STR", AttributeValue.Score(16));
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetAttributeAsync_HostOnly_HostCanEditAnySheet()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var sheetId = SeedSheetForPlayer(player.Id);
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { SheetEditByOthers = SheetEditPolicy.HostOnly });

            var update = _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Score(16));
            Assert.IsTrue(update.IsSuccess);
        }

        [TestMethod]
        public void UpdateSheetAttributeAsync_Anyone_PlayerCanEditOthersSheet()
        {
            var owner = EngineTestFactory.RegisterPlayer(_state, "Owner");
            var other = EngineTestFactory.RegisterPlayer(_state, "Other");
            var sheetId = SeedSheetForPlayer(owner.Id);
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { SheetEditByOthers = SheetEditPolicy.Anyone });

            var update = _engine.UpdateSheetAttributeAsync(_state, other, sheetId, "STR", AttributeValue.Score(16));
            Assert.IsTrue(update.IsSuccess);
        }

        [TestMethod]
        public void UpdateSheetAttributeAsync_UnknownAttribute_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var update = _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "UNKNOWN", AttributeValue.Score(1));
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetAttributeAsync_TypeMismatch_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var update = _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Text("hello"));
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetFreeFieldsAsync_NullableHpAccepted()
        {
            var sheetId = SeedSheetForPlayer(null);
            var update = _engine.UpdateSheetFreeFieldsAsync(_state, _host, sheetId, "Bob", "notes", null, null);
            Assert.IsTrue(update.IsSuccess);
            Assert.IsNull(_state.Sheets[sheetId].Hp);
            Assert.IsNull(_state.Sheets[sheetId].MaxHp);
        }

        [TestMethod]
        public void UpdateSheetFreeFieldsAsync_EmptyCharacterName_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var update = _engine.UpdateSheetFreeFieldsAsync(_state, _host, sheetId, "  ", "", null, null);
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetFreeFieldsAsync_HostCaller_UpdatesAllFields()
        {
            var sheetId = SeedSheetForPlayer(null);
            var update = _engine.UpdateSheetFreeFieldsAsync(_state, _host, sheetId, "Bob", "wounded", 7, 12);
            Assert.IsTrue(update.IsSuccess);

            var sheet = _state.Sheets[sheetId];
            Assert.AreEqual("Bob", sheet.CharacterName);
            Assert.AreEqual("wounded", sheet.Notes);
            Assert.AreEqual(7, sheet.Hp);
            Assert.AreEqual(12, sheet.MaxHp);
        }

        [TestMethod]
        public void DeleteSheetAsync_HostCaller_RemovesAndUnlinksTokens()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Map");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            // Register player BEFORE activating so SetActiveMap auto-spawns a token+sheet.
            EngineTestFactory.RegisterPlayer(_state);
            _engine.SetActiveMapAsync(_state, _host, mapId);

            var tokenId = _state.Maps.Single(m => m.Id == mapId).Tokens.Single().Id;
            var sheetId = _state.Maps.Single(m => m.Id == mapId).Tokens.Single().SheetId!.Value;

            var del = _engine.DeleteSheetAsync(_state, _host, sheetId);
            Assert.IsTrue(del.IsSuccess);
            Assert.IsFalse(_state.Sheets.ContainsKey(sheetId));
            Assert.IsNull(_state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId).SheetId);
        }

        [TestMethod]
        public void DeleteSheetAsync_NonHostCaller_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var player = EngineTestFactory.RegisterPlayer(_state);
            var del = _engine.DeleteSheetAsync(_state, player, sheetId);
            Assert.IsTrue(del.IsFailure);
        }

        [TestMethod]
        public void DeleteSheetAsync_UnknownSheetId_ReturnsError()
        {
            var del = _engine.DeleteSheetAsync(_state, _host, Guid.NewGuid());
            Assert.IsTrue(del.IsFailure);
        }

        [TestMethod]
        public void ChangeSchemaAsync_KeepsMatchingValueByName()
        {
            var sheetId = SeedSheetForPlayer(null);
            _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Score(18));

            var newSchema = AttributeSchema.FromPreset(AttributePreset.DnD5ePlusCommonSkills);
            var change = _engine.ChangeSchemaAsync(_state, _host, newSchema);
            Assert.IsTrue(change.IsSuccess);
            Assert.AreEqual(18, _state.Sheets[sheetId].Values["STR"].IntValue);
        }

        [TestMethod]
        public void ChangeSchemaAsync_TypeMismatch_ResetsToDefault()
        {
            // Use a custom schema where "STR" is now a Modifier instead of Score.
            var sheetId = SeedSheetForPlayer(null);
            _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Score(18));

            var newSchema = new AttributeSchema(AttributePreset.Custom, new[]
            {
                new AttributeRow("STR", AttributeValueType.Modifier, AttributeValue.Modifier(0)),
            });
            var change = _engine.ChangeSchemaAsync(_state, _host, newSchema);
            Assert.IsTrue(change.IsSuccess);
            Assert.AreEqual(AttributeValueType.Modifier, _state.Sheets[sheetId].Values["STR"].Type);
            Assert.AreEqual(0, _state.Sheets[sheetId].Values["STR"].IntValue);
        }

        [TestMethod]
        public void ChangeSchemaAsync_DropsUnknownAttributes()
        {
            var sheetId = SeedSheetForPlayer(null);
            var newSchema = new AttributeSchema(AttributePreset.Custom, new[]
            {
                new AttributeRow("OnlyMe", AttributeValueType.Modifier, AttributeValue.Modifier(0)),
            });
            _engine.ChangeSchemaAsync(_state, _host, newSchema);
            Assert.IsTrue(_state.Sheets[sheetId].Values.ContainsKey("OnlyMe"));
            Assert.IsFalse(_state.Sheets[sheetId].Values.ContainsKey("STR"));
        }

        [TestMethod]
        public void ChangeSchemaAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var newSchema = AttributeSchema.FromPreset(AttributePreset.SimpleD20);
            var change = _engine.ChangeSchemaAsync(_state, player, newSchema);
            Assert.IsTrue(change.IsFailure);
        }

        // ── DuplicateSheetAsync ───────────────────────────────────────────────

        [TestMethod]
        public void DuplicateSheetAsync_HostCaller_ClonesEveryFieldAndClearsOwner()
        {
            var sheetId = SeedSheetForPlayer(null);
            _engine.UpdateSheetAttributeAsync(_state, _host, sheetId, "STR", AttributeValue.Score(18));
            _engine.UpdateSheetFreeFieldsAsync(_state, _host, sheetId, "Wizard", "lonely", 5, 9);
            _engine.UpdateSheetArmorClassAsync(_state, _host, sheetId, 17);
            _engine.UpdateSheetColorAsync(_state, _host, sheetId, "#abcdef");

            var dup = _engine.DuplicateSheetAsync(_state, _host, sheetId);
            Assert.IsTrue(dup.TryGetSuccess(out var newId));
            Assert.AreNotEqual(sheetId, newId);

            var clone = _state.Sheets[newId];
            Assert.AreEqual("Wizard (copy)", clone.CharacterName);
            Assert.AreEqual("lonely", clone.Notes);
            Assert.AreEqual(5, clone.Hp);
            Assert.AreEqual(9, clone.MaxHp);
            Assert.AreEqual(17, clone.ArmorClass);
            Assert.AreEqual("#abcdef", clone.Color);
            Assert.AreEqual(18, clone.Values["STR"].IntValue);
            Assert.IsNull(clone.OwnerUserId);
            Assert.IsNull(clone.RepresentsUserId);
        }

        [TestMethod]
        public void DuplicateSheetAsync_NonHostCaller_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var player = EngineTestFactory.RegisterPlayer(_state);
            var dup = _engine.DuplicateSheetAsync(_state, player, sheetId);
            Assert.IsTrue(dup.IsFailure);
        }

        [TestMethod]
        public void DuplicateSheetAsync_UnknownSheetId_ReturnsError()
        {
            var dup = _engine.DuplicateSheetAsync(_state, _host, Guid.NewGuid());
            Assert.IsTrue(dup.IsFailure);
        }

        // ── UpdateSheetArmorClassAsync ────────────────────────────────────────

        [TestMethod]
        public void UpdateSheetArmorClassAsync_HostCaller_SetsAndClears()
        {
            var sheetId = SeedSheetForPlayer(null);
            Assert.IsTrue(_engine.UpdateSheetArmorClassAsync(_state, _host, sheetId, 14).IsSuccess);
            Assert.AreEqual(14, _state.Sheets[sheetId].ArmorClass);

            Assert.IsTrue(_engine.UpdateSheetArmorClassAsync(_state, _host, sheetId, null).IsSuccess);
            Assert.IsNull(_state.Sheets[sheetId].ArmorClass);
        }

        // ── UpdateSheetColorAsync ─────────────────────────────────────────────

        [TestMethod]
        public void UpdateSheetColorAsync_PropagatesViaResolveColor()
        {
            // Create a map with a token linked to the sheet; sheet color should
            // win over token color in the resolver.
            var c = _engine.CreateMapAsync(_state, _host, "Arena");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            _engine.SetActiveMapAsync(_state, _host, mapId);
            var sheetId = SeedSheetForPlayer(null);
            var tcr = _engine.CreateTokenForSheetOnMapAsync(_state, _host, sheetId, mapId);
            Assert.IsTrue(tcr.TryGetSuccess(out var tokenId));

            Assert.IsTrue(_engine.UpdateSheetColorAsync(_state, _host, sheetId, "#ff00ff").IsSuccess);
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual("#ff00ff", token.ResolveColor(_state.Sheets));
        }

        // ── UpdateSheetScopeAsync ─────────────────────────────────────────────

        [TestMethod]
        public void UpdateSheetScopeAsync_HostCaller_PinsAndClears()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Arena");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var sheetId = SeedSheetForPlayer(null);

            Assert.IsTrue(_engine.UpdateSheetScopeAsync(_state, _host, sheetId, mapId).IsSuccess);
            Assert.AreEqual(mapId, _state.Sheets[sheetId].ScopedMapId);

            Assert.IsTrue(_engine.UpdateSheetScopeAsync(_state, _host, sheetId, null).IsSuccess);
            Assert.IsNull(_state.Sheets[sheetId].ScopedMapId);
        }

        [TestMethod]
        public void UpdateSheetScopeAsync_NonHostCaller_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.UpdateSheetScopeAsync(_state, player, sheetId, null);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void UpdateSheetScopeAsync_UnknownMapId_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var result = _engine.UpdateSheetScopeAsync(_state, _host, sheetId, Guid.NewGuid());
            Assert.IsTrue(result.IsFailure);
        }

        // ── CreateTokenForSheetOnMapAsync ─────────────────────────────────────

        [TestMethod]
        public void CreateTokenForSheetOnMapAsync_HostCaller_SpawnsLinkedToken()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Arena");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            _engine.SetActiveMapAsync(_state, _host, mapId);
            var sheetId = SeedSheetForPlayer(null);

            var tcr = _engine.CreateTokenForSheetOnMapAsync(_state, _host, sheetId, mapId);
            Assert.IsTrue(tcr.TryGetSuccess(out var tokenId));

            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(sheetId, token.SheetId);
            Assert.AreEqual("Sheet", token.Name);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
        }

        [TestMethod]
        public void CreateTokenForSheetOnMapAsync_UnknownMap_ReturnsError()
        {
            var sheetId = SeedSheetForPlayer(null);
            var tcr = _engine.CreateTokenForSheetOnMapAsync(_state, _host, sheetId, Guid.NewGuid());
            Assert.IsTrue(tcr.IsFailure);
        }

        [TestMethod]
        public void CreateTokenForSheetOnMapAsync_NonHostCaller_ReturnsError()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Arena");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var sheetId = SeedSheetForPlayer(null);
            var player = EngineTestFactory.RegisterPlayer(_state);
            var tcr = _engine.CreateTokenForSheetOnMapAsync(_state, player, sheetId, mapId);
            Assert.IsTrue(tcr.IsFailure);
        }

        private Guid SeedSheetForPlayer(Guid? ownerUserId)
        {
            var result = _engine.CreateSheetAsync(_state, _host, ownerUserId, "Sheet");
            Assert.IsTrue(result.TryGetSuccess(out var sheetId));
            return sheetId;
        }
    }
}
