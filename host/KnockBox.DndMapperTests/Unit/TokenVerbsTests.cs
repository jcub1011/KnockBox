using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class TokenVerbsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private Guid CreateAndActivateMap(string name = "Map")
        {
            var c = _engine.CreateMapAsync(_state, _host, name);
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var setActive = _engine.SetActiveMapAsync(_state, _host, mapId);
            Assert.IsTrue(setActive.IsSuccess);
            return mapId;
        }

        // For tests that need player tokens to exist, register players BEFORE activating
        // so SetActiveMap auto-spawns them.
        private (Guid mapId, User[] players) CreateMapWithPlayers(params string[] playerNames)
        {
            var c = _engine.CreateMapAsync(_state, _host, "Map");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var players = new User[playerNames.Length];
            for (int i = 0; i < playerNames.Length; i++)
                players[i] = EngineTestFactory.RegisterPlayer(_state, playerNames[i]);
            var setActive = _engine.SetActiveMapAsync(_state, _host, mapId);
            Assert.IsTrue(setActive.IsSuccess);
            return (mapId, players);
        }

        [TestMethod]
        public void SpawnPlayerTokenInternal_DefaultColorIsDerivedFromPlayerName()
        {
            var (mapId, players) = CreateMapWithPlayers("P1", "P2");
            var map = _state.Maps.Single(m => m.Id == mapId);
            var p1Token = map.Tokens.First(t => t.OwnerUserId == players[0].Id);
            var p2Token = map.Tokens.First(t => t.OwnerUserId == players[1].Id);
            Assert.AreEqual(DefaultColorPalette.FromName("P1"), p1Token.Color);
            Assert.AreEqual(DefaultColorPalette.FromName("P2"), p2Token.Color);
            Assert.AreNotEqual(p1Token.Color, p2Token.Color);
        }

        [TestMethod]
        public void SpawnPlayerTokenInternal_ReusesExistingSheetForPlayer()
        {
            var (mapA, players) = CreateMapWithPlayers("Alice");
            var sheetIdOnA = _state.Maps.Single(m => m.Id == mapA).Tokens.Single().SheetId;

            var c = _engine.CreateMapAsync(_state, _host, "B");
            Assert.IsTrue(c.TryGetSuccess(out var mapB));
            _engine.SetActiveMapAsync(_state, _host, mapB);

            var sheetIdOnB = _state.Maps.Single(m => m.Id == mapB).Tokens.Single(t => t.OwnerUserId == players[0].Id).SheetId;
            Assert.IsNotNull(sheetIdOnA);
            Assert.AreEqual(sheetIdOnA, sheetIdOnB);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_HostCaller_DefaultColorIsDerivedFromName()
        {
            var mapId = CreateAndActivateMap();
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
            Assert.AreEqual(DefaultColorPalette.FromName("Goblin"), token.Color);
            Assert.IsNull(token.OwnerUserId);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_NonHostPlayerWithSettingDisabled_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state);
            var spawn = _engine.SpawnNpcTokenAsync(_state, player, mapId, "Goblin");
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_NonHostPlayerWithSettingEnabled_AssignsCallerAsOwner()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state);
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { PlayersCanCreateNPCs = true });

            var spawn = _engine.SpawnNpcTokenAsync(_state, player, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(player.Id, token.OwnerUserId);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_EmptyName_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "  ");
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_WithRepresents_DefaultColorStillDerivedFromTokenName()
        {
            // Represents-an-existing-player NPCs (DMPCs, abandoned-player stand-ins)
            // still seed their default color from the NPC's own name; the host can
            // always override later if they want to mirror the player's color.
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");

            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Alice (DMPC)", player.Id);
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
            Assert.AreEqual(player.Id, token.RepresentsUserId);
            Assert.AreEqual(DefaultColorPalette.FromName("Alice (DMPC)"), token.Color);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_NonHostWithRepresents_ReturnsError()
        {
            // Even with PlayersCanCreateNPCs on, a non-host caller cannot create an
            // NPC that stands in for a specific player.
            var mapId = CreateAndActivateMap();
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { PlayersCanCreateNPCs = true });
            var caller = EngineTestFactory.RegisterPlayer(_state, "Caller");
            var represented = EngineTestFactory.RegisterPlayer(_state, "Other");

            var spawn = _engine.SpawnNpcTokenAsync(_state, caller, mapId, "x", represented.Id);
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_HostWithRepresents_UnknownMapId_ReturnsError()
        {
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, Guid.NewGuid(), "x", representsUserId: null);
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void MoveTokenAsync_OwnerOrHost_OwnerCanMoveOwnToken()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            var tokenId = _state.Maps.Single(m => m.Id == mapId).Tokens.Single().Id;

            // Engine snaps to cell center (x.5, y.5) when SnapToGrid is on, even
            // if a stale client sent an intersection-aligned coordinate.
            var move = _engine.MoveTokenAsync(_state, players[0], tokenId, 5, 5);
            Assert.IsTrue(move.IsSuccess);
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(4.5, token.X);
            Assert.AreEqual(4.5, token.Y);
        }

        [TestMethod]
        public void MoveTokenAsync_SnapEnabled_SnapsToCellCenter_EvenForOffCenterInput()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            var tokenId = _state.Maps.Single(m => m.Id == mapId).Tokens.Single().Id;

            // Off-center input (3.2, 7.8) should land at the nearest cell center.
            // SnapToGridHelper.Snap rounds (x - 0.5) and adds 0.5 back, so the
            // result is the cell whose center is closest.
            var move = _engine.MoveTokenAsync(_state, players[0], tokenId, 3.2, 7.8);
            Assert.IsTrue(move.IsSuccess);
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(3.5, token.X);
            Assert.AreEqual(7.5, token.Y);
        }

        [TestMethod]
        public void MoveTokenAsync_OwnerOrHost_NonOwnerNonHostCannotMove()
        {
            var (mapId, players) = CreateMapWithPlayers("Owner", "Other");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.First(t => t.OwnerUserId == players[0].Id);

            var move = _engine.MoveTokenAsync(_state, players[1], token.Id, 5, 5);
            Assert.IsTrue(move.IsFailure);
        }

        [TestMethod]
        public void MoveTokenAsync_OwnerOrHost_HostCanMoveAnyToken()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var move = _engine.MoveTokenAsync(_state, _host, token.Id, 7, 7);
            Assert.IsTrue(move.IsSuccess);
        }

        [TestMethod]
        public void MoveTokenAsync_Anyone_PlayerCanMoveAnotherPlayersToken()
        {
            var (mapId, players) = CreateMapWithPlayers("P1", "P2");
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { TokenMovement = TokenMovementPolicy.Anyone });

            var p1Token = _state.Maps.Single(m => m.Id == mapId).Tokens.First(t => t.OwnerUserId == players[0].Id);
            var move = _engine.MoveTokenAsync(_state, players[1], p1Token.Id, 3, 3);
            Assert.IsTrue(move.IsSuccess);
        }

        [TestMethod]
        public void MoveTokenAsync_HostOnly_PlayerCannotMoveOwnToken()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { TokenMovement = TokenMovementPolicy.HostOnly });

            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();
            var move = _engine.MoveTokenAsync(_state, players[0], token.Id, 1, 1);
            Assert.IsTrue(move.IsFailure);
        }

        [TestMethod]
        public void MoveTokenAsync_OutOfBoundsCoordinates_ReturnsError()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var move = _engine.MoveTokenAsync(_state, _host, token.Id, -1, 5);
            Assert.IsTrue(move.IsFailure);
        }

        [TestMethod]
        public void MoveTokenAsync_UnknownTokenId_ReturnsError()
        {
            CreateAndActivateMap();
            var move = _engine.MoveTokenAsync(_state, _host, Guid.NewGuid(), 0, 0);
            Assert.IsTrue(move.IsFailure);
        }

        [TestMethod]
        public void UpdateTokenAsync_HostCanUpdateAnyToken()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            var tokenId = _state.Maps.Single(m => m.Id == mapId).Tokens.Single().Id;

            var update = _engine.UpdateTokenAsync(_state, _host, tokenId, "NewName", "#aabbcc", TokenIconKind.Solid);
            Assert.IsTrue(update.IsSuccess);
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual("NewName", token.Name);
            Assert.AreEqual("#aabbcc", token.Color);
            Assert.AreEqual(TokenIconKind.Solid, token.IconKind);
        }

        [TestMethod]
        public void UpdateTokenAsync_OwnerCanUpdateOwnPlayerToken()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var update = _engine.UpdateTokenAsync(_state, players[0], token.Id, "MyHero", "#112233", TokenIconKind.Initial);
            Assert.IsTrue(update.IsSuccess);
        }

        [TestMethod]
        public void UpdateTokenAsync_NonOwnerNonHost_CannotUpdate()
        {
            var (mapId, players) = CreateMapWithPlayers("Owner", "Other");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.First(t => t.OwnerUserId == players[0].Id);

            var update = _engine.UpdateTokenAsync(_state, players[1], token.Id, "x", "#fff", TokenIconKind.Solid);
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateTokenAsync_EmptyName_ReturnsError()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();
            var update = _engine.UpdateTokenAsync(_state, _host, token.Id, "  ", "#000", TokenIconKind.Initial);
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void RemoveTokenAsync_HostCaller_RemovesNpcToken()
        {
            var mapId = CreateAndActivateMap();
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));

            var remove = _engine.RemoveTokenAsync(_state, _host, tokenId);
            Assert.IsTrue(remove.IsSuccess);
            Assert.DoesNotContain(t => t.Id == tokenId, _state.Maps.Single(m => m.Id == mapId).Tokens);
        }

        [TestMethod]
        public void RemoveTokenAsync_PlayerToken_ReturnsErrorEvenForHost()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            var playerToken = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var remove = _engine.RemoveTokenAsync(_state, _host, playerToken.Id);
            Assert.IsTrue(remove.IsFailure);
        }

        [TestMethod]
        public void RemoveTokenAsync_UnknownTokenId_ReturnsError()
        {
            CreateAndActivateMap();
            var remove = _engine.RemoveTokenAsync(_state, _host, Guid.NewGuid());
            Assert.IsTrue(remove.IsFailure);
        }

        [TestMethod]
        public void SetTokenHiddenAsync_HostCaller_FlipsHidden()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            var tokenId = _state.Maps.Single(m => m.Id == mapId).Tokens.Single().Id;

            var hide = _engine.SetTokenHiddenAsync(_state, _host, tokenId, true);
            Assert.IsTrue(hide.IsSuccess);
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.IsTrue(token.Hidden);
        }

        [TestMethod]
        public void SetTokenHiddenAsync_NonHostCaller_ReturnsError()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();
            var hide = _engine.SetTokenHiddenAsync(_state, players[0], token.Id, true);
            Assert.IsTrue(hide.IsFailure);
        }

        [TestMethod]
        public void SetTokenHiddenAsync_UnknownTokenId_ReturnsError()
        {
            CreateAndActivateMap();
            var hide = _engine.SetTokenHiddenAsync(_state, _host, Guid.NewGuid(), true);
            Assert.IsTrue(hide.IsFailure);
        }

        [TestMethod]
        public void PlayerUnregistered_ConvertsAllPlayerTokensToOrphanedNpcTokens()
        {
            var mapA = CreateAndActivateMap("A");
            // Register player + auto-spawn on A
            var player = UserFactory.Create("Alice", Guid.NewGuid().ToString());
            var reg = _state.RegisterPlayer(player);
            Assert.IsTrue(reg.TryGetSuccess(out var token));

            // Activate A again to spawn (RegisterPlayer doesn't trigger spawn — only SetActiveMap does mid-pre-Start)
            _engine.SetActiveMapAsync(_state, _host, mapA);
            var c = _engine.CreateMapAsync(_state, _host, "B");
            Assert.IsTrue(c.TryGetSuccess(out var mapB));
            _engine.SetActiveMapAsync(_state, _host, mapB);

            // Confirm player has a token on each map
            Assert.Contains(t => t.OwnerUserId == player.Id, _state.Maps.Single(m => m.Id == mapA).Tokens);
            Assert.Contains(t => t.OwnerUserId == player.Id, _state.Maps.Single(m => m.Id == mapB).Tokens);

            token.Dispose();

            foreach (var map in _state.Maps)
            {
                foreach (var t in map.Tokens.Where(t => t.RepresentsUserId == player.Id))
                {
                    Assert.AreEqual(TokenType.NPCToken, t.Type);
                    Assert.IsNull(t.OwnerUserId);
                    Assert.AreEqual(player.Id, t.RepresentsUserId);
                }
            }
        }

        [TestMethod]
        public void PlayerUnregistered_OrphansPlayerSheetWithAuditTrail()
        {
            CreateAndActivateMap("A");
            var player = UserFactory.Create("Alice", Guid.NewGuid().ToString());
            var reg = _state.RegisterPlayer(player);
            Assert.IsTrue(reg.TryGetSuccess(out var token));

            // SetActiveMap is what triggers per-map spawn pre-start; pump it once
            // more so Alice has a token (and therefore a session-scoped sheet).
            _engine.SetActiveMapAsync(_state, _host, _state.Maps.Single().Id);
            var sheetId = _state.Sheets.Values.Single(s => s.OwnerUserId == player.Id).Id;

            token.Dispose();

            var orphaned = _state.Sheets[sheetId];
            Assert.IsNull(orphaned.OwnerUserId, "Sheet should be released to NPC ownership.");
            Assert.AreEqual(player.Id, orphaned.RepresentsUserId, "Sheet should retain audit trail of original player.");
            Assert.DoesNotContain(s => s.OwnerUserId == player.Id, _state.Sheets.Values);
        }

        // ── AssignCharacterToPlayerAsync ──────────────────────────────────────────

        [TestMethod]
        public void AssignCharacterToPlayerAsync_NpcWithSheet_ReassignsBothAtomically()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var tokenId));
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin Sheet").TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, tokenId, sheetId).IsSuccess);

            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, tokenId, player.Id);
            Assert.IsTrue(result.IsSuccess, $"AssignCharacterToPlayerAsync failed: {result}");

            var token = _state.Maps.Single().Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(player.Id, token.OwnerUserId);
            Assert.IsNull(token.RepresentsUserId);

            var sheet = _state.Sheets[sheetId];
            Assert.AreEqual(player.Id, sheet.OwnerUserId);
            Assert.IsNull(sheet.RepresentsUserId);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_TokenWithoutSheet_ReassignsTokenOnly()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var tokenId));

            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, tokenId, player.Id);
            Assert.IsTrue(result.IsSuccess);

            var token = _state.Maps.Single().Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(player.Id, token.OwnerUserId);
            Assert.IsNull(token.SheetId);
            Assert.DoesNotContain(s => s.OwnerUserId == player.Id, _state.Sheets.Values);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_TargetAlreadyOwnsSheet_ReturnsErrorAndLeavesStateUntouched()
        {
            // Activate map FIRST so SetActiveMap doesn't auto-spawn — we want a player
            // with a sheet but no PlayerToken so we isolate the sheet-conflict guard
            // (otherwise the player-token-on-map guard would fire first).
            var mapId = CreateAndActivateMap();
            var alice = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: alice.Id, "Alice's prior sheet")
                .TryGetSuccess(out var aliceSheetId));

            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var npcId));
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin Sheet").TryGetSuccess(out var goblinSheetId));
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, npcId, goblinSheetId).IsSuccess);

            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, npcId, alice.Id);
            Assert.IsTrue(result.IsFailure);

            var npc = _state.Maps.Single().Tokens.Single(t => t.Id == npcId);
            Assert.AreEqual(TokenType.NPCToken, npc.Type);
            Assert.IsNull(npc.OwnerUserId);
            Assert.IsNull(_state.Sheets[goblinSheetId].OwnerUserId);
            Assert.AreEqual(alice.Id, _state.Sheets[aliceSheetId].OwnerUserId);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_NonHostCaller_ReturnsError()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice", "Bob");
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var npcId));

            var result = _engine.AssignCharacterToPlayerAsync(_state, players[0], npcId, players[1].Id);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_TargetNotRegistered_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var npcId));

            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, npcId, "not-a-registered-id");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_PlayerTokenSource_ReturnsError()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice", "Bob");
            var aliceToken = _state.Maps.Single().Tokens.First(t => t.OwnerUserId == players[0].Id);

            // Bob has a sheet already (auto-spawned), so we use the conflicting-owner guard
            // as a secondary signal; but the primary block is the "token is already a player token" check.
            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, aliceToken.Id, players[1].Id);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_OrphanedAcrossMaps_PromotesEverySiblingToken()
        {
            // A character with tokens on multiple maps (one sheet shared across all of them)
            // must be promoted on every map atomically — otherwise the new player ends up
            // with one PlayerToken and a bunch of stale orphan NPCs for the same character.
            var mapA = CreateAndActivateMap("A");
            var alice = UserFactory.Create("Alice", Guid.NewGuid().ToString());
            var aliceReg = _state.RegisterPlayer(alice);
            Assert.IsTrue(aliceReg.TryGetSuccess(out var aliceToken));
            _engine.SetActiveMapAsync(_state, _host, mapA); // spawn for Alice on A

            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "B").TryGetSuccess(out var mapB));
            _engine.SetActiveMapAsync(_state, _host, mapB); // spawn for Alice on B (reuses sheet)

            var sheetId = _state.Sheets.Values.Single(s => s.OwnerUserId == alice.Id).Id;
            aliceToken.Dispose();

            // Both maps' tokens should now be orphan NPCs that share the sheet.
            Assert.IsTrue(_state.Maps.All(m => m.Tokens.Single(t => t.SheetId == sheetId).Type == TokenType.NPCToken));

            var bob = EngineTestFactory.RegisterPlayer(_state, "Bob");
            var tokenOnA = _state.Maps.Single(m => m.Id == mapA).Tokens.Single(t => t.SheetId == sheetId);
            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, tokenOnA.Id, bob.Id);
            Assert.IsTrue(result.IsSuccess, $"Assign failed: {result}");

            foreach (var map in _state.Maps)
            {
                var t = map.Tokens.Single(tk => tk.SheetId == sheetId);
                Assert.AreEqual(TokenType.PlayerToken, t.Type, $"Token on map {map.Name} should be PlayerToken.");
                Assert.AreEqual(bob.Id, t.OwnerUserId);
                Assert.IsNull(t.RepresentsUserId);
            }

            var sheet = _state.Sheets[sheetId];
            Assert.AreEqual(bob.Id, sheet.OwnerUserId);
            Assert.IsNull(sheet.RepresentsUserId);
        }

        // ── AssignSheetToPlayerAsync ──────────────────────────────────────────────

        [TestMethod]
        public void AssignSheetToPlayerAsync_OrphanedSheetWithTokens_PromotesEverything()
        {
            var mapId = CreateAndActivateMap();
            var alice = UserFactory.Create("Alice", Guid.NewGuid().ToString());
            var aliceReg = _state.RegisterPlayer(alice);
            Assert.IsTrue(aliceReg.TryGetSuccess(out var aliceToken));
            _engine.SetActiveMapAsync(_state, _host, mapId);

            var sheetId = _state.Sheets.Values.Single(s => s.OwnerUserId == alice.Id).Id;
            aliceToken.Dispose();
            Assert.IsNull(_state.Sheets[sheetId].OwnerUserId);

            var bob = EngineTestFactory.RegisterPlayer(_state, "Bob");
            var result = _engine.AssignSheetToPlayerAsync(_state, _host, sheetId, bob.Id);
            Assert.IsTrue(result.IsSuccess, $"AssignSheetToPlayerAsync failed: {result}");

            var sheet = _state.Sheets[sheetId];
            Assert.AreEqual(bob.Id, sheet.OwnerUserId);
            Assert.IsNull(sheet.RepresentsUserId);
            var token = _state.Maps.Single().Tokens.Single(t => t.SheetId == sheetId);
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(bob.Id, token.OwnerUserId);
        }

        [TestMethod]
        public void AssignSheetToPlayerAsync_SheetWithoutAnyTokens_TransfersOwnershipOnly()
        {
            // A host-built NPC sheet with no tokens attached anywhere — the typical
            // "I prepared a character for a player who just joined" workflow.
            CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Pre-built").TryGetSuccess(out var sheetId));

            var result = _engine.AssignSheetToPlayerAsync(_state, _host, sheetId, player.Id);
            Assert.IsTrue(result.IsSuccess, $"AssignSheetToPlayerAsync failed: {result}");

            var sheet = _state.Sheets[sheetId];
            Assert.AreEqual(player.Id, sheet.OwnerUserId);
            Assert.IsNull(sheet.RepresentsUserId);
            Assert.DoesNotContain(t => t.SheetId == sheetId, _state.Maps.SelectMany(m => m.Tokens));
        }

        [TestMethod]
        public void AssignSheetToPlayerAsync_SheetAlreadyPlayerOwned_TransfersToNewOwner()
        {
            // Re-assigning a character from one player to another is allowed —
            // the host uses this to hand off a character mid-session. The target
            // must not already own a different sheet (separate test).
            var (_, players) = CreateMapWithPlayers("Alice");
            var aliceSheetId = _state.Sheets.Values.Single(s => s.OwnerUserId == players[0].Id).Id;
            var bob = EngineTestFactory.RegisterPlayer(_state, "Bob");

            var result = _engine.AssignSheetToPlayerAsync(_state, _host, aliceSheetId, bob.Id);
            Assert.IsTrue(result.IsSuccess, $"AssignSheetToPlayerAsync failed: {result}");

            var sheet = _state.Sheets[aliceSheetId];
            Assert.AreEqual(bob.Id, sheet.OwnerUserId);
            Assert.IsNull(sheet.RepresentsUserId);
            foreach (var token in _state.Maps.SelectMany(m => m.Tokens).Where(t => t.SheetId == aliceSheetId))
            {
                Assert.AreEqual(TokenType.PlayerToken, token.Type);
                Assert.AreEqual(bob.Id, token.OwnerUserId);
            }
        }

        [TestMethod]
        public void AssignSheetToPlayerAsync_TargetAlreadyOwnsSheet_ReturnsError()
        {
            CreateAndActivateMap();
            var alice = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: alice.Id, "Alice").TryGetSuccess(out _));
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "NPC").TryGetSuccess(out var npcSheetId));

            var result = _engine.AssignSheetToPlayerAsync(_state, _host, npcSheetId, alice.Id);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AssignSheetToPlayerAsync_NonHost_ReturnsError()
        {
            CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "NPC").TryGetSuccess(out var sheetId));

            var result = _engine.AssignSheetToPlayerAsync(_state, player, sheetId, player.Id);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AssignSheetToPlayerAsync_UnknownSheetId_ReturnsError()
        {
            CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            var result = _engine.AssignSheetToPlayerAsync(_state, _host, Guid.NewGuid(), player.Id);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void AssignCharacterToPlayerAsync_OrphanedNpc_FullLeaveThenAssignCycle()
        {
            // Build a session where Alice spawns and then leaves; Bob (a different player) joins later
            // and the host hands the orphaned character to Bob.
            var mapId = CreateAndActivateMap();
            var alice = UserFactory.Create("Alice", Guid.NewGuid().ToString());
            var aliceReg = _state.RegisterPlayer(alice);
            Assert.IsTrue(aliceReg.TryGetSuccess(out var aliceToken));
            _engine.SetActiveMapAsync(_state, _host, mapId); // triggers spawn for Alice

            var orphanTokenId = _state.Maps.Single().Tokens.Single(t => t.OwnerUserId == alice.Id).Id;
            var orphanSheetId = _state.Sheets.Values.Single(s => s.OwnerUserId == alice.Id).Id;

            // Alice leaves — engine converts token to an orphan NPC and sheet to orphan
            aliceToken.Dispose();
            Assert.AreEqual(TokenType.NPCToken, _state.Maps.Single().Tokens.Single(t => t.Id == orphanTokenId).Type);
            Assert.IsNull(_state.Sheets[orphanSheetId].OwnerUserId);

            // Bob joins; host hands the orphan to Bob.
            var bob = EngineTestFactory.RegisterPlayer(_state, "Bob");
            var result = _engine.AssignCharacterToPlayerAsync(_state, _host, orphanTokenId, bob.Id);
            Assert.IsTrue(result.IsSuccess, $"Assign failed: {result}");

            var token = _state.Maps.Single().Tokens.Single(t => t.Id == orphanTokenId);
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(bob.Id, token.OwnerUserId);
            Assert.IsNull(token.RepresentsUserId);

            var sheet = _state.Sheets[orphanSheetId];
            Assert.AreEqual(bob.Id, sheet.OwnerUserId);
            Assert.IsNull(sheet.RepresentsUserId);
        }

        [TestMethod]
        public void RegisterPlayer_AfterStart_IsRejectedByPlatform()
        {
            // Locks in the platform contract this milestone depends on (no mid-session join).
            var mapId = CreateAndActivateMap();
            EngineTestFactory.RegisterPlayer(_state, "PreStart");
            var startResult = _engine.StartAsync(_host, _state).GetAwaiter().GetResult();
            Assert.IsTrue(startResult.IsSuccess);

            var newcomer = UserFactory.Create("Late", Guid.NewGuid().ToString());
            var reg = _state.RegisterPlayer(newcomer);
            Assert.IsTrue(reg.IsFailure, "RegisterPlayer must reject after StartAsync flips IsJoinable to false.");
        }

        [TestMethod]
        public void SpawnPlayerTokenAsync_HostSpawnsForRegisteredPlayer_AddsToken()
        {
            // Activate the map BEFORE registering the player so SetActiveMap's auto-spawn
            // doesn't fire — we want the explicit verb to be the thing that creates the token.
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");

            var spawn = _engine.SpawnPlayerTokenAsync(_state, _host, player.Id);
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));

            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(player.Id, token.OwnerUserId);
        }

        [TestMethod]
        public void SpawnPlayerTokenAsync_PlayerSpawnsForSelf_AddsToken()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Self");

            var spawn = _engine.SpawnPlayerTokenAsync(_state, player, player.Id);
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));
            Assert.Contains(t => t.Id == tokenId, _state.Maps.Single(m => m.Id == mapId).Tokens);
        }

        [TestMethod]
        public void SpawnPlayerTokenAsync_PlayerSpawnsForOther_ReturnsError()
        {
            CreateAndActivateMap();
            var caller = EngineTestFactory.RegisterPlayer(_state, "Caller");
            var target = EngineTestFactory.RegisterPlayer(_state, "Target");

            var spawn = _engine.SpawnPlayerTokenAsync(_state, caller, target.Id);
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnPlayerTokenAsync_NoActiveMap_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");
            var spawn = _engine.SpawnPlayerTokenAsync(_state, _host, player.Id);
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnPlayerTokenAsync_TargetUserNotRegistered_ReturnsError()
        {
            CreateAndActivateMap();
            var spawn = _engine.SpawnPlayerTokenAsync(_state, _host, Guid.NewGuid().ToString());
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnPlayerTokenAsync_EmptyUserId_ReturnsError()
        {
            CreateAndActivateMap();
            var spawn = _engine.SpawnPlayerTokenAsync(_state, _host, "  ");
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_NonHostNonRegisteredCaller_ReturnsError()
        {
            // Setting allows player NPC creation, but caller isn't a registered player.
            // Defensive engine-layer check should still reject.
            var mapId = CreateAndActivateMap();
            _engine.UpdateSettingsAsync(_state, _host, new DndMapperSettings { PlayersCanCreateNPCs = true });

            var stranger = UserFactory.Create("Stranger", Guid.NewGuid().ToString());
            var spawn = _engine.SpawnNpcTokenAsync(_state, stranger, mapId, "Goblin");
            Assert.IsTrue(spawn.IsFailure);
        }

        // ── SetTokenSheetAsync ────────────────────────────────────────────────────

        [TestMethod]
        public void SetTokenSheetAsync_HostAttachesAndDetaches()
        {
            var mapId = CreateAndActivateMap();
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var tokenId));
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin Sheet").TryGetSuccess(out var sheetId));

            var attach = _engine.SetTokenSheetAsync(_state, _host, tokenId, sheetId);
            Assert.IsTrue(attach.IsSuccess);
            Assert.AreEqual(sheetId, _state.Maps[0].Tokens.Single(t => t.Id == tokenId).SheetId);

            var detach = _engine.SetTokenSheetAsync(_state, _host, tokenId, null);
            Assert.IsTrue(detach.IsSuccess);
            Assert.IsNull(_state.Maps[0].Tokens.Single(t => t.Id == tokenId).SheetId);
        }

        [TestMethod]
        public void SetTokenSheetAsync_NonHost_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var tokenId));
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.SetTokenSheetAsync(_state, player, tokenId, null);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetTokenSheetAsync_UnknownSheet_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var tokenId));

            var result = _engine.SetTokenSheetAsync(_state, _host, tokenId, Guid.NewGuid());
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetTokenSheetAsync_UnknownToken_ReturnsError()
        {
            var result = _engine.SetTokenSheetAsync(_state, _host, Guid.NewGuid(), null);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetTokenSheetAsync_PlayerOwnedSheet_ReturnsError()
        {
            var (mapId, _) = CreateMapWithPlayers("Alice");
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin").TryGetSuccess(out var npcId));
            var playerSheetId = _state.Sheets.Values.Single().Id; // Alice's auto-spawned sheet

            var result = _engine.SetTokenSheetAsync(_state, _host, npcId, playerSheetId);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetTokenSheetAsync_SheetAttachedToOtherToken_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin1").TryGetSuccess(out var npc1));
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin2").TryGetSuccess(out var npc2));
            Assert.IsTrue(_engine.CreateSheetAsync(_state, _host, ownerUserId: null, "Goblin Sheet").TryGetSuccess(out var sheetId));
            Assert.IsTrue(_engine.SetTokenSheetAsync(_state, _host, npc1, sheetId).IsSuccess);

            var result = _engine.SetTokenSheetAsync(_state, _host, npc2, sheetId);
            Assert.IsTrue(result.IsFailure);
            // Original attachment unchanged.
            Assert.AreEqual(sheetId, _state.Maps.Single().Tokens.Single(t => t.Id == npc1).SheetId);
            Assert.IsNull(_state.Maps.Single().Tokens.Single(t => t.Id == npc2).SheetId);
        }

        // ── SetTokenRepresentsAsync ───────────────────────────────────────────────

        [TestMethod]
        public void SetTokenRepresentsAsync_NpcToken_SetAndClear()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Doppel")
                .TryGetSuccess(out var tokenId));

            var set = _engine.SetTokenRepresentsAsync(_state, _host, tokenId, players[0].Id);
            Assert.IsTrue(set.IsSuccess);
            Assert.AreEqual(players[0].Id, _state.Maps[0].Tokens.Single(t => t.Id == tokenId).RepresentsUserId);

            var clear = _engine.SetTokenRepresentsAsync(_state, _host, tokenId, null);
            Assert.IsTrue(clear.IsSuccess);
            Assert.IsNull(_state.Maps[0].Tokens.Single(t => t.Id == tokenId).RepresentsUserId);
        }

        [TestMethod]
        public void SetTokenRepresentsAsync_PlayerToken_ReturnsError()
        {
            // Player tokens are auto-managed and cannot stand in for another player.
            var (_, players) = CreateMapWithPlayers("Alice", "Bob");
            var aliceToken = _state.Maps.Single().Tokens.Single(t => t.OwnerUserId == players[0].Id);

            var result = _engine.SetTokenRepresentsAsync(_state, _host, aliceToken.Id, players[1].Id);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetTokenRepresentsAsync_UnknownPlayer_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Doppel")
                .TryGetSuccess(out var tokenId));

            var result = _engine.SetTokenRepresentsAsync(_state, _host, tokenId, "not-a-real-player-id");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetTokenRepresentsAsync_NonHost_ReturnsError()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            Assert.IsTrue(_engine.SpawnNpcTokenAsync(_state, _host, mapId, "Doppel")
                .TryGetSuccess(out var tokenId));

            var result = _engine.SetTokenRepresentsAsync(_state, players[0], tokenId, players[0].Id);
            Assert.IsTrue(result.IsFailure);
        }
    }
}
