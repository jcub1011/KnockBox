using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class MapVerbsTests
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
        public void CreateMapAsync_HostCaller_AppendsToMaps()
        {
            var result = _engine.CreateMapAsync(_state, _host, "Tavern");
            Assert.IsTrue(result.TryGetSuccess(out var newId));
            Assert.HasCount(1, _state.Maps);
            Assert.AreEqual(0, _state.Maps[0].ListOrder);
            Assert.AreEqual(newId, _state.Maps[0].Id);
            Assert.AreEqual("Tavern", _state.Maps[0].Name);
        }

        [TestMethod]
        public void CreateMapAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var result = _engine.CreateMapAsync(_state, player, "Tavern");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void CreateMapAsync_EmptyName_ReturnsError()
        {
            var result = _engine.CreateMapAsync(_state, _host, "  ");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RenameMapAsync_HostCaller_UpdatesName()
        {
            var create = _engine.CreateMapAsync(_state, _host, "Initial");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));

            var rename = _engine.RenameMapAsync(_state, _host, mapId, "Renamed");
            Assert.IsTrue(rename.IsSuccess);
            Assert.AreEqual("Renamed", _state.Maps.Single().Name);
        }

        [TestMethod]
        public void RenameMapAsync_EmptyName_ReturnsError()
        {
            var create = _engine.CreateMapAsync(_state, _host, "Initial");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));

            var rename = _engine.RenameMapAsync(_state, _host, mapId, "");
            Assert.IsTrue(rename.IsFailure);
        }

        [TestMethod]
        public void RenameMapAsync_UnknownMapId_ReturnsError()
        {
            var rename = _engine.RenameMapAsync(_state, _host, Guid.NewGuid(), "x");
            Assert.IsTrue(rename.IsFailure);
        }

        [TestMethod]
        public void RenameMapAsync_NonHostCaller_ReturnsError()
        {
            var create = _engine.CreateMapAsync(_state, _host, "Initial");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(_state);

            var rename = _engine.RenameMapAsync(_state, player, mapId, "x");
            Assert.IsTrue(rename.IsFailure);
        }

        [TestMethod]
        public void DeleteMapAsync_HostCaller_RemovesMapFromList()
        {
            var create = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));

            var del = _engine.DeleteMapAsync(_state, _host, mapId);
            Assert.IsTrue(del.IsSuccess);
            Assert.IsEmpty(_state.Maps);
        }

        [TestMethod]
        public void DeleteMapAsync_DeletingActiveMap_ShiftsActiveMapIdToNextByListOrder()
        {
            var create1 = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(create1.TryGetSuccess(out var mapA));
            var create2 = _engine.CreateMapAsync(_state, _host, "B");
            Assert.IsTrue(create2.TryGetSuccess(out var mapB));
            var setActive = _engine.SetActiveMapAsync(_state, _host, mapA);
            Assert.IsTrue(setActive.IsSuccess);

            var del = _engine.DeleteMapAsync(_state, _host, mapA);
            Assert.IsTrue(del.IsSuccess);
            Assert.AreEqual(mapB, _state.ActiveMapId);
        }

        [TestMethod]
        public void DeleteMapAsync_DeletingLastMap_SetsActiveMapIdToNull()
        {
            var create = _engine.CreateMapAsync(_state, _host, "Only");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));
            _engine.SetActiveMapAsync(_state, _host, mapId);

            var del = _engine.DeleteMapAsync(_state, _host, mapId);
            Assert.IsTrue(del.IsSuccess);
            Assert.IsNull(_state.ActiveMapId);
        }

        [TestMethod]
        public void DeleteMapAsync_NonHostCaller_ReturnsError()
        {
            var create = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(_state);

            var del = _engine.DeleteMapAsync(_state, player, mapId);
            Assert.IsTrue(del.IsFailure);
        }

        [TestMethod]
        public void DuplicateMapAsync_HostCaller_DeepClonesGridButEmptyTokens()
        {
            var create = _engine.CreateMapAsync(_state, _host, "Source");
            Assert.IsTrue(create.TryGetSuccess(out var sourceId));
            var source = _state.Maps.Single(m => m.Id == sourceId);
            source.Grid.WidthCells = 50;
            source.Tokens.Add(new Token { Id = Guid.NewGuid(), Name = "x" });

            var dup = _engine.DuplicateMapAsync(_state, _host, sourceId);
            Assert.IsTrue(dup.TryGetSuccess(out var dupId));
            var clone = _state.Maps.Single(m => m.Id == dupId);

            Assert.AreEqual(50, clone.Grid.WidthCells);
            Assert.AreNotSame(source.Grid, clone.Grid);
            Assert.IsEmpty(clone.Tokens);
            Assert.AreEqual("Source (copy)", clone.Name);
        }

        [TestMethod]
        public void DuplicateMapAsync_NonHostCaller_ReturnsError()
        {
            var create = _engine.CreateMapAsync(_state, _host, "Source");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(_state);
            var dup = _engine.DuplicateMapAsync(_state, player, mapId);
            Assert.IsTrue(dup.IsFailure);
        }

        [TestMethod]
        public void DuplicateMapAsync_UnknownMapId_ReturnsError()
        {
            var dup = _engine.DuplicateMapAsync(_state, _host, Guid.NewGuid());
            Assert.IsTrue(dup.IsFailure);
        }

        [TestMethod]
        public void ReorderMapsAsync_PermutationOfIds_UpdatesListOrder()
        {
            var c1 = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c1.TryGetSuccess(out var a));
            var c2 = _engine.CreateMapAsync(_state, _host, "B");
            Assert.IsTrue(c2.TryGetSuccess(out var b));
            var c3 = _engine.CreateMapAsync(_state, _host, "C");
            Assert.IsTrue(c3.TryGetSuccess(out var c));

            var reorder = _engine.ReorderMapsAsync(_state, _host, new[] { c, a, b });
            Assert.IsTrue(reorder.IsSuccess);

            Assert.AreEqual(0, _state.Maps.Single(m => m.Id == c).ListOrder);
            Assert.AreEqual(1, _state.Maps.Single(m => m.Id == a).ListOrder);
            Assert.AreEqual(2, _state.Maps.Single(m => m.Id == b).ListOrder);
        }

        [TestMethod]
        public void ReorderMapsAsync_MissingId_ReturnsError()
        {
            var c1 = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c1.TryGetSuccess(out var a));
            var c2 = _engine.CreateMapAsync(_state, _host, "B");
            Assert.IsTrue(c2.TryGetSuccess(out _));

            var reorder = _engine.ReorderMapsAsync(_state, _host, new[] { a });
            Assert.IsTrue(reorder.IsFailure);
        }

        [TestMethod]
        public void ReorderMapsAsync_NonHostCaller_ReturnsError()
        {
            var c1 = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c1.TryGetSuccess(out var a));
            var player = EngineTestFactory.RegisterPlayer(_state);
            var reorder = _engine.ReorderMapsAsync(_state, player, new[] { a });
            Assert.IsTrue(reorder.IsFailure);
        }

        [TestMethod]
        public void SetActiveMapAsync_HostCaller_UpdatesActiveMapId()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var a));
            var setActive = _engine.SetActiveMapAsync(_state, _host, a);
            Assert.IsTrue(setActive.IsSuccess);
            Assert.AreEqual(a, _state.ActiveMapId);
        }

        [TestMethod]
        public void SetActiveMapAsync_RegisteredPlayerWithoutTokenOnMap_AutoSpawnsPlayerToken()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(_state, "Alice");

            _engine.SetActiveMapAsync(_state, _host, mapId);

            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.HasCount(1, map.Tokens);
            var token = map.Tokens[0];
            Assert.AreEqual(TokenType.PlayerToken, token.Type);
            Assert.AreEqual(player.Id, token.OwnerUserId);
        }

        [TestMethod]
        public void SetActiveMapAsync_PlayerAlreadyHasTokenOnTargetMap_DoesNotDuplicate()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            EngineTestFactory.RegisterPlayer(_state, "Alice");
            _engine.SetActiveMapAsync(_state, _host, mapId);
            // calling SetActiveMap again should not add a second token
            _engine.SetActiveMapAsync(_state, _host, mapId);

            Assert.HasCount(1, _state.Maps.Single(m => m.Id == mapId).Tokens);
        }

        [TestMethod]
        public void SetActiveMapAsync_UnknownMapId_ReturnsError()
        {
            var setActive = _engine.SetActiveMapAsync(_state, _host, Guid.NewGuid());
            Assert.IsTrue(setActive.IsFailure);
        }

        [TestMethod]
        public void SetActiveMapAsync_NonHostCaller_ReturnsError()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(_state);
            var setActive = _engine.SetActiveMapAsync(_state, player, mapId);
            Assert.IsTrue(setActive.IsFailure);
        }

        [TestMethod]
        public void UpdateGridAsync_WidthBelowMinimum_ReturnsError()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));

            var update = _engine.UpdateGridAsync(_state, _host, mapId, new GridConfig { WidthCells = 4 });
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateGridAsync_HeightAboveMaximum_ReturnsError()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));

            var update = _engine.UpdateGridAsync(_state, _host, mapId, new GridConfig { HeightCells = 201 });
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void UpdateGridAsync_HostCaller_ReplacesGridConfig()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));

            var newGrid = new GridConfig
            {
                WidthCells = 60,
                HeightCells = 40,
                CellPixels = 64,
                ShowGridLines = false,
                SnapToGrid = false,
                LineColor = "#ff0000",
            };
            var update = _engine.UpdateGridAsync(_state, _host, mapId, newGrid);
            Assert.IsTrue(update.IsSuccess);

            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.AreEqual(60, map.Grid.WidthCells);
            Assert.AreEqual(40, map.Grid.HeightCells);
            Assert.AreEqual(64, map.Grid.CellPixels);
            Assert.IsFalse(map.Grid.ShowGridLines);
            Assert.IsFalse(map.Grid.SnapToGrid);
            Assert.AreEqual("#ff0000", map.Grid.LineColor);
            Assert.AreNotSame(newGrid, map.Grid);
        }

        [TestMethod]
        public void UpdateGridAsync_NonHostCaller_ReturnsError()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            var player = EngineTestFactory.RegisterPlayer(_state);
            var update = _engine.UpdateGridAsync(_state, player, mapId, new GridConfig());
            Assert.IsTrue(update.IsFailure);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_EvenGrid_PlacesAtCellCenter()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Even");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            Assert.IsTrue(_engine.UpdateGridAsync(_state, _host,
                mapId, new GridConfig { WidthCells = 10, HeightCells = 10 }).IsSuccess);

            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));

            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            // Floor-then-center: 10/2 -> 5, +0.5 -> 5.5. Centers must never sit
            // on a grid intersection (e.g. 5.0).
            Assert.AreEqual(5.5, token.X, 1e-9);
            Assert.AreEqual(5.5, token.Y, 1e-9);
        }

        [TestMethod]
        public void SpawnNpcTokenAsync_OddGrid_PlacesAtCellCenter()
        {
            var c = _engine.CreateMapAsync(_state, _host, "Odd");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            Assert.IsTrue(_engine.UpdateGridAsync(_state, _host,
                mapId, new GridConfig { WidthCells = 11, HeightCells = 11 }).IsSuccess);

            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));

            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            // Floor(11/2)=5, +0.5 -> 5.5 (still a cell center for odd grids).
            Assert.AreEqual(5.5, token.X, 1e-9);
            Assert.AreEqual(5.5, token.Y, 1e-9);
        }

        [TestMethod]
        public void UpdateGridAsync_ShrinkGrid_ClampsTokenPositionsToNewBounds()
        {
            var c = _engine.CreateMapAsync(_state, _host, "A");
            Assert.IsTrue(c.TryGetSuccess(out var mapId));
            Assert.IsTrue(_engine.SetActiveMapAsync(_state, _host, mapId).IsSuccess);

            var spawn = _engine.SpawnNpcTokenAsync(_state, _host, mapId, "Goblin");
            Assert.IsTrue(spawn.TryGetSuccess(out var tokenId));

            var move = _engine.MoveTokenAsync(_state, _host, tokenId, 25.5, 15.5);
            Assert.IsTrue(move.IsSuccess);

            var newGrid = new GridConfig { WidthCells = 10, HeightCells = 8 };
            var update = _engine.UpdateGridAsync(_state, _host, mapId, newGrid);
            Assert.IsTrue(update.IsSuccess);

            var token = _state.Maps.Single(m => m.Id == mapId).Tokens.Single(t => t.Id == tokenId);
            Assert.IsLessThanOrEqualTo(10 - 0.5 + 1e-6, token.X, $"Token X {token.X} should be clamped within new width.");
            Assert.IsLessThanOrEqualTo(8 - 0.5 + 1e-6, token.Y, $"Token Y {token.Y} should be clamped within new height.");
            Assert.IsGreaterThanOrEqualTo(0.5 - 1e-6, token.X);
            Assert.IsGreaterThanOrEqualTo(0.5 - 1e-6, token.Y);
        }
    }
}
