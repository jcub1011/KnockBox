using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class FogOfWarVerbsTests
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
        public void PaintFog_Host_SetsCellsToTrue()
        {
            var mapId = CreateMap();
            var cells = new[] { (1, 1), (2, 1), (3, 1) };

            var result = _engine.PaintFogAsync(_state, _host, mapId, cells, fogged: true);

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsTrue(map.IsFogged(1, 1));
            Assert.IsTrue(map.IsFogged(2, 1));
            Assert.IsTrue(map.IsFogged(3, 1));
            Assert.IsFalse(map.IsFogged(0, 1));
            Assert.IsFalse(map.IsFogged(4, 1));
        }

        [TestMethod]
        public void PaintFog_Host_ClearsCellsWhenFoggedFalse()
        {
            var mapId = CreateMap();
            Assert.IsTrue(_engine.HideCellsAsync(_state, _host, mapId, new[] { (5, 5), (6, 5) }).IsSuccess);
            Assert.IsTrue(_state.Maps.Single(m => m.Id == mapId).IsFogged(5, 5));

            var result = _engine.PaintFogAsync(_state, _host, mapId, new[] { (5, 5), (6, 5) }, fogged: false);

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsFalse(map.IsFogged(5, 5));
            Assert.IsFalse(map.IsFogged(6, 5));
        }

        [TestMethod]
        public void PaintFog_NonHost_ReturnsFailure()
        {
            var mapId = CreateMap();
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.PaintFogAsync(_state, player, mapId, new[] { (0, 0) }, fogged: true);

            Assert.IsTrue(result.IsFailure);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsFalse(map.IsFogged(0, 0));
        }

        [TestMethod]
        public void PaintFog_UnknownMapId_ReturnsFailure()
        {
            var result = _engine.PaintFogAsync(_state, _host, Guid.NewGuid(), new[] { (0, 0) }, fogged: true);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void PaintFog_EmptyCellList_ReturnsSuccess_NoMutation()
        {
            var mapId = CreateMap();
            var map = _state.Maps.Single(m => m.Id == mapId);

            var result = _engine.PaintFogAsync(_state, _host, mapId, Array.Empty<(int, int)>(), fogged: true);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsEmpty(map.FogMask);
        }

        [TestMethod]
        public void PaintFog_OutOfBoundsCells_OnlyInBoundsApplied()
        {
            var mapId = CreateMap(); // default 30×20
            var cells = new[] { (-1, -1), (0, 0), (30, 0), (0, 20), (29, 19) };

            var result = _engine.PaintFogAsync(_state, _host, mapId, cells, fogged: true);

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsTrue(map.IsFogged(0, 0));
            Assert.IsTrue(map.IsFogged(29, 19));
            Assert.IsFalse(map.IsFogged(-1, -1));
            // Cells exactly at WidthCells / HeightCells are out of bounds.
            Assert.IsFalse(map.IsFogged(30, 0));
            Assert.IsFalse(map.IsFogged(0, 20));
        }

        [TestMethod]
        public void FillMapWithFog_AllocatesAndSetsAllCellsFogged()
        {
            var mapId = CreateMap();

            var result = _engine.FillMapWithFogAsync(_state, _host, mapId);

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            for (var cy = 0; cy < map.Grid.HeightCells; cy++)
                for (var cx = 0; cx < map.Grid.WidthCells; cx++)
                    Assert.IsTrue(map.IsFogged(cx, cy), $"Cell ({cx}, {cy}) should be fogged.");
        }

        [TestMethod]
        public void FillMapWithFog_DoesNotSetBitsBeyondGridSize()
        {
            // 3x3 grid: 9 cells → 2 bytes (16 bits). Last 7 bits should be zeroed
            // so the serialized mask stays exact and IsFogged on out-of-bounds
            // (where bounds-check would otherwise hide a bit leak) still returns
            // false for cells inside the byte but past the grid.
            var mapId = CreateMap();
            Assert.IsTrue(_engine.UpdateGridAsync(
                _state, _host, mapId,
                new GridConfig { WidthCells = 3, HeightCells = 3 }).IsSuccess);

            var result = _engine.FillMapWithFogAsync(_state, _host, mapId);
            Assert.IsTrue(result.IsSuccess);

            var map = _state.Maps.Single(m => m.Id == mapId);
            // 9 bits set → byte 0 = 0xFF (8 cells), byte 1 = 0b00000001 (1 cell).
            Assert.AreEqual(2, map.FogMask.Length);
            Assert.AreEqual(0xFF, map.FogMask[0]);
            Assert.AreEqual(0x01, map.FogMask[1]);

            for (var cy = 0; cy < 3; cy++)
                for (var cx = 0; cx < 3; cx++)
                    Assert.IsTrue(map.IsFogged(cx, cy));
        }

        [TestMethod]
        public void ClearAllFog_ResetsMaskToEmpty()
        {
            var mapId = CreateMap();
            var fill = _engine.FillMapWithFogAsync(_state, _host, mapId);
            Assert.IsTrue(fill.IsSuccess);
            Assert.IsTrue(_state.Maps.Single(m => m.Id == mapId).FogMask.Length > 0);

            var result = _engine.ClearAllFogAsync(_state, _host, mapId);

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsEmpty(map.FogMask);
            Assert.IsFalse(map.IsFogged(0, 0));
        }

        [TestMethod]
        public void RevealCells_DelegatesToPaintFogFalse()
        {
            var mapId = CreateMap();
            Assert.IsTrue(_engine.HideCellsAsync(_state, _host, mapId, new[] { (2, 2), (3, 3) }).IsSuccess);

            var result = _engine.RevealCellsAsync(_state, _host, mapId, new[] { (2, 2), (3, 3) });

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsFalse(map.IsFogged(2, 2));
            Assert.IsFalse(map.IsFogged(3, 3));
        }

        [TestMethod]
        public void HideCells_DelegatesToPaintFogTrue()
        {
            var mapId = CreateMap();

            var result = _engine.HideCellsAsync(_state, _host, mapId, new[] { (4, 4), (5, 5) });

            Assert.IsTrue(result.IsSuccess);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsTrue(map.IsFogged(4, 4));
            Assert.IsTrue(map.IsFogged(5, 5));
        }

        [TestMethod]
        public void PaintFog_OnMapA_DoesNotAffectMapB()
        {
            var mapA = CreateMap("A");
            var mapB = CreateMap("B");

            var result = _engine.PaintFogAsync(_state, _host, mapA, new[] { (0, 0), (1, 1) }, fogged: true);
            Assert.IsTrue(result.IsSuccess);

            var a = _state.Maps.Single(m => m.Id == mapA);
            var b = _state.Maps.Single(m => m.Id == mapB);
            Assert.IsTrue(a.IsFogged(0, 0));
            Assert.IsTrue(a.IsFogged(1, 1));
            Assert.IsFalse(b.IsFogged(0, 0));
            Assert.IsFalse(b.IsFogged(1, 1));
            Assert.IsEmpty(b.FogMask);
        }

        [TestMethod]
        public void FillMapWithFog_NonHost_ReturnsFailure()
        {
            var mapId = CreateMap();
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.FillMapWithFogAsync(_state, player, mapId);

            Assert.IsTrue(result.IsFailure);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsEmpty(map.FogMask);
        }

        [TestMethod]
        public void ClearAllFog_NonHost_ReturnsFailure()
        {
            var mapId = CreateMap();
            _engine.FillMapWithFogAsync(_state, _host, mapId);
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.ClearAllFogAsync(_state, player, mapId);

            Assert.IsTrue(result.IsFailure);
            var map = _state.Maps.Single(m => m.Id == mapId);
            Assert.IsTrue(map.FogMask.Length > 0);
        }

        private Guid CreateMap(string name = "M")
        {
            var create = _engine.CreateMapAsync(_state, _host, name);
            Assert.IsTrue(create.TryGetSuccess(out var id), $"CreateMapAsync failed: {create}");
            return id;
        }
    }
}
