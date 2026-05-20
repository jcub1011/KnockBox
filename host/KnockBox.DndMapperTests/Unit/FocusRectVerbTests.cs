using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class FocusRectVerbTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
        }

        private Guid SeedMap()
        {
            var r = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(r.TryGetSuccess(out var id));
            return id;
        }

        [TestMethod]
        public void SetFocusRect_StoresRectOnState_WhenInputsValid()
        {
            var mapId = SeedMap();
            var r = _engine.SetFocusRect(_state, _host, mapId, 2, 3, 10, 8);
            Assert.IsTrue(r.IsSuccess);
            Assert.IsNotNull(_state.FocusRect);
            Assert.AreEqual(mapId, _state.FocusRect!.MapId);
            Assert.AreEqual(2, _state.FocusRect.X);
            Assert.AreEqual(3, _state.FocusRect.Y);
            Assert.AreEqual(10, _state.FocusRect.Width);
            Assert.AreEqual(8, _state.FocusRect.Height);
        }

        [TestMethod]
        public void SetFocusRect_UnknownMap_Rejected()
        {
            var r = _engine.SetFocusRect(_state, _host, Guid.NewGuid(), 0, 0, 5, 5);
            Assert.IsTrue(r.IsFailure);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void SetFocusRect_NonHost_Rejected()
        {
            var mapId = SeedMap();
            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.SetFocusRect(_state, player, mapId, 0, 0, 5, 5);
            Assert.IsTrue(r.IsFailure);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void SetFocusRect_ZeroOrNegativeSize_Rejected()
        {
            var mapId = SeedMap();
            Assert.IsTrue(_engine.SetFocusRect(_state, _host, mapId, 1, 1, 0, 5).IsFailure);
            Assert.IsTrue(_engine.SetFocusRect(_state, _host, mapId, 1, 1, 5, 0).IsFailure);
            Assert.IsTrue(_engine.SetFocusRect(_state, _host, mapId, 1, 1, -3, 5).IsFailure);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void SetFocusRect_NonFiniteCoordinates_Rejected()
        {
            var mapId = SeedMap();
            Assert.IsTrue(_engine.SetFocusRect(_state, _host, mapId, double.NaN, 0, 5, 5).IsFailure);
            Assert.IsTrue(_engine.SetFocusRect(_state, _host, mapId, 0, double.PositiveInfinity, 5, 5).IsFailure);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void SetFocusRect_ClampsToMapBounds()
        {
            // Default map is 30 × 20 cells. Drag a rect that starts inside but
            // extends past the right + bottom edges — the engine should crop it
            // to the on-canvas portion (rather than rejecting outright).
            var mapId = SeedMap();
            var r = _engine.SetFocusRect(_state, _host, mapId, 25, 15, 100, 100);
            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual(25, _state.FocusRect!.X);
            Assert.AreEqual(15, _state.FocusRect.Y);
            Assert.AreEqual(5, _state.FocusRect.Width);   // 30 - 25
            Assert.AreEqual(5, _state.FocusRect.Height);  // 20 - 15
        }

        [TestMethod]
        public void SetFocusRect_ClampsNegativeOrigin()
        {
            var mapId = SeedMap();
            var r = _engine.SetFocusRect(_state, _host, mapId, -5, -2, 10, 7);
            Assert.IsTrue(r.IsSuccess);
            Assert.AreEqual(0, _state.FocusRect!.X);
            Assert.AreEqual(0, _state.FocusRect.Y);
            Assert.AreEqual(5, _state.FocusRect.Width);   // -5 + 10 → 5
            Assert.AreEqual(5, _state.FocusRect.Height);  // -2 + 7 → 5
        }

        [TestMethod]
        public void SetFocusRect_EntirelyOffMap_Rejected()
        {
            // Rect lies wholly outside the map; clamping collapses it below
            // the minimum-size threshold and the engine rejects.
            var mapId = SeedMap();
            var r = _engine.SetFocusRect(_state, _host, mapId, 100, 100, 5, 5);
            Assert.IsTrue(r.IsFailure);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void SetFocusRect_BelowMinSize_Rejected()
        {
            // 0.1 × 0.1 is below the 0.25 min — would zoom the display in so
            // far that any jitter would feel uncontrollable.
            var mapId = SeedMap();
            var r = _engine.SetFocusRect(_state, _host, mapId, 1, 1, 0.1, 0.1);
            Assert.IsTrue(r.IsFailure);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void ClearFocusRect_NullsOutTheRect()
        {
            var mapId = SeedMap();
            _engine.SetFocusRect(_state, _host, mapId, 2, 3, 10, 8);
            Assert.IsNotNull(_state.FocusRect);

            var r = _engine.ClearFocusRect(_state, _host);
            Assert.IsTrue(r.IsSuccess);
            Assert.IsNull(_state.FocusRect);
        }

        [TestMethod]
        public void ClearFocusRect_NonHost_Rejected()
        {
            var mapId = SeedMap();
            _engine.SetFocusRect(_state, _host, mapId, 2, 3, 10, 8);

            var player = EngineTestFactory.RegisterPlayer(_state);
            var r = _engine.ClearFocusRect(_state, player);
            Assert.IsTrue(r.IsFailure);
            Assert.IsNotNull(_state.FocusRect);
        }

        [TestMethod]
        public void SetFocusRect_ReplacesPreviousValue()
        {
            var mapId = SeedMap();
            _engine.SetFocusRect(_state, _host, mapId, 0, 0, 5, 5);
            _engine.SetFocusRect(_state, _host, mapId, 10, 10, 8, 4);
            Assert.AreEqual(10, _state.FocusRect!.X);
            Assert.AreEqual(10, _state.FocusRect.Y);
            Assert.AreEqual(8, _state.FocusRect.Width);
            Assert.AreEqual(4, _state.FocusRect.Height);
        }
    }
}
