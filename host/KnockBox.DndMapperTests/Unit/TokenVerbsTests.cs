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
        public void SpawnPlayerTokenInternal_AssignsPaletteColorByPlayerSlot()
        {
            var (mapId, players) = CreateMapWithPlayers("P1", "P2");
            var map = _state.Maps.Single(m => m.Id == mapId);
            var p1Token = map.Tokens.First(t => t.OwnerUserId == players[0].Id);
            var p2Token = map.Tokens.First(t => t.OwnerUserId == players[1].Id);
            Assert.AreEqual("#1f77b4", p1Token.Color);
            Assert.AreEqual("#ff7f0e", p2Token.Color);
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
        public void SpawnNpcTokenAsync_HostCaller_CreatesNeutralColorToken()
        {
            var mapId = CreateAndActivateMap();
            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.NPCToken, token.Type);
            Assert.AreEqual("#888", token.Color);
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
        public void SpawnHostExtraTokenAsync_HostCaller_AssignsRepresentsUserIdColor()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");

            var spawn = _engine.SpawnHostExtraTokenAsync(_state, _host, mapId, "Alice (DMPC)", player.Id);
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.AreEqual(TokenType.HostExtraToken, token.Type);
            Assert.AreEqual(player.Id, token.RepresentsUserId);
            Assert.AreEqual("#1f77b4", token.Color); // slot 0 palette color
        }

        [TestMethod]
        public void SpawnHostExtraTokenAsync_NonHostCaller_ReturnsError()
        {
            var mapId = CreateAndActivateMap();
            var player = EngineTestFactory.RegisterPlayer(_state);
            var spawn = _engine.SpawnHostExtraTokenAsync(_state, player, mapId, "x", null);
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void SpawnHostExtraTokenAsync_UnknownMapId_ReturnsError()
        {
            var spawn = _engine.SpawnHostExtraTokenAsync(_state, _host, Guid.NewGuid(), "x", null);
            Assert.IsTrue(spawn.IsFailure);
        }

        [TestMethod]
        public void MoveTokenAsync_OwnerOrHost_OwnerCanMoveOwnToken()
        {
            var (mapId, players) = CreateMapWithPlayers("Alice");
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var move = _engine.MoveTokenAsync(_state, players[0], token.Id, 5, 5);
            Assert.IsTrue(move.IsSuccess);
            Assert.AreEqual(5, token.X);
            Assert.AreEqual(5, token.Y);
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
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var update = _engine.UpdateTokenAsync(_state, _host, token.Id, "NewName", "#aabbcc", TokenIconKind.Solid);
            Assert.IsTrue(update.IsSuccess);
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
            Assert.IsFalse(_state.Maps.Single(m => m.Id == mapId).Tokens.Any(t => t.Id == tokenId));
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
            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single();

            var hide = _engine.SetTokenHiddenAsync(_state, _host, token.Id, true);
            Assert.IsTrue(hide.IsSuccess);
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
        public void PlayerUnregistered_ConvertsAllPlayerTokensToHostExtraTokens()
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
            Assert.IsTrue(_state.Maps.Single(m => m.Id == mapA).Tokens.Any(t => t.OwnerUserId == player.Id));
            Assert.IsTrue(_state.Maps.Single(m => m.Id == mapB).Tokens.Any(t => t.OwnerUserId == player.Id));

            token.Dispose();

            foreach (var map in _state.Maps)
            {
                foreach (var t in map.Tokens.Where(t => t.RepresentsUserId == player.Id))
                {
                    Assert.AreEqual(TokenType.HostExtraToken, t.Type);
                    Assert.IsNull(t.OwnerUserId);
                    Assert.AreEqual(player.Id, t.RepresentsUserId);
                }
            }
            // Sheet remains
            Assert.IsTrue(_state.Sheets.Values.Any(s => s.OwnerUserId == player.Id));
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
            Assert.IsTrue(_state.Maps.Single(m => m.Id == mapId).Tokens.Any(t => t.Id == tokenId));
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
    }
}
