using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit
{
    // Pairs with Map.FogVersion / Map.ImagesVersion. MapCanvas memoizes the
    // fog polygon and visible-image projection keyed on these counters; if
    // a mutating verb forgets to bump, the canvas would render stale state.
    [TestClass]
    public class MapVersionCounterTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private Guid _mapId;

        [TestInitialize]
        public void Setup()
        {
            (_engine, _state, _host, _) = EngineTestFactory.Build();
            var create = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(create.TryGetSuccess(out _mapId));
        }

        private Map Map => _state.Maps.Single(m => m.Id == _mapId);

        private static MapImage SeedImage(long bytes = 100)
            => new()
            {
                Id = Guid.NewGuid(),
                ContentType = "image/png",
                ShareToken = Guid.NewGuid(),
                Width = 10,
                Height = 10,
                Opacity = 1.0,
                ByteSize = bytes,
            };

        // ── FogVersion ────────────────────────────────────────────────────────────

        [TestMethod]
        public void SetFogged_ActualMutation_BumpsFogVersion()
        {
            var before = Map.FogVersion;
            Map.SetFogged(0, 0, true);
            Assert.AreEqual(before + 1, Map.FogVersion);
        }

        [TestMethod]
        public void SetFogged_NoOp_DoesNotBumpFogVersion()
        {
            Map.SetFogged(0, 0, true);
            var before = Map.FogVersion;
            // Same value: must be a no-op for versioning.
            Map.SetFogged(0, 0, true);
            Assert.AreEqual(before, Map.FogVersion);
        }

        [TestMethod]
        public void SetFogged_OutOfBounds_DoesNotBumpFogVersion()
        {
            var before = Map.FogVersion;
            Map.SetFogged(-1, -1, true);
            Assert.AreEqual(before, Map.FogVersion);
        }

        [TestMethod]
        public void PaintFogAsync_BumpsFogVersion()
        {
            var before = Map.FogVersion;
            var result = _engine.PaintFogAsync(_state, _host, _mapId, new[] { (1, 1), (2, 1) }, fogged: true);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.FogVersion);
        }

        [TestMethod]
        public void FillMapWithFogAsync_BumpsFogVersion()
        {
            var before = Map.FogVersion;
            var result = _engine.FillMapWithFogAsync(_state, _host, _mapId);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.FogVersion);
        }

        [TestMethod]
        public void ClearAllFogAsync_BumpsFogVersion_WhenMaskWasNonEmpty()
        {
            _engine.FillMapWithFogAsync(_state, _host, _mapId);
            var before = Map.FogVersion;
            var result = _engine.ClearAllFogAsync(_state, _host, _mapId);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.FogVersion);
        }

        [TestMethod]
        public void ClearAllFogAsync_AlreadyEmpty_DoesNotBumpFogVersion()
        {
            // Fresh map starts with an empty mask; clearing again should be a no-op.
            var before = Map.FogVersion;
            var result = _engine.ClearAllFogAsync(_state, _host, _mapId);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(before, Map.FogVersion);
        }

        // ── ImagesVersion ─────────────────────────────────────────────────────────

        [TestMethod]
        public void AddImageAsync_BumpsImagesVersion()
        {
            var before = Map.ImagesVersion;
            var result = _engine.AddImageAsync(_state, _host, _mapId, SeedImage());
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void UpdateImageTransformAsync_BumpsImagesVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesVersion;
            var result = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id, 1, 2, 3, 4, 10, 0.5);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void SetImageHiddenAsync_BumpsImagesVersion_OnChange()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesVersion;
            var result = _engine.SetImageHiddenAsync(_state, _host, _mapId, img.Id, true);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void SetImageHiddenAsync_NoChange_DoesNotBumpImagesVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            _engine.SetImageHiddenAsync(_state, _host, _mapId, img.Id, true);
            var before = Map.ImagesVersion;
            // Already hidden — second call should be a no-op for versioning.
            _engine.SetImageHiddenAsync(_state, _host, _mapId, img.Id, true);
            Assert.AreEqual(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void SetImageLockedAsync_BumpsImagesVersion_OnChange()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesVersion;
            var result = _engine.SetImageLockedAsync(_state, _host, _mapId, img.Id, true);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void ReorderImageLayerAsync_BumpsImagesVersion()
        {
            var a = SeedImage();
            var b = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, a);
            _engine.AddImageAsync(_state, _host, _mapId, b);
            var before = Map.ImagesVersion;
            var result = _engine.ReorderImageLayerAsync(_state, _host, _mapId, a.Id, 1);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void RemoveImageAsync_BumpsImagesVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesVersion;
            var result = _engine.RemoveImageAsync(_state, _host, _mapId, img.Id);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_BumpsImagesVersion_OnChange()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesVersion;
            var result = _engine.UpdateImageShareTokenAsync(_state, _host, _mapId, img.Id, Guid.NewGuid());
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesVersion);
        }

        // ── ImagesMembershipVersion ───────────────────────────────────────────────
        // Narrower counter that only tracks (id, locked) set changes — the data
        // MapCanvas's JS image-drag module actually cares about. Pure transform
        // edits must NOT bump it.

        [TestMethod]
        public void AddImageAsync_BumpsImagesMembershipVersion()
        {
            var before = Map.ImagesMembershipVersion;
            var result = _engine.AddImageAsync(_state, _host, _mapId, SeedImage());
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesMembershipVersion);
        }

        [TestMethod]
        public void RemoveImageAsync_BumpsImagesMembershipVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesMembershipVersion;
            var result = _engine.RemoveImageAsync(_state, _host, _mapId, img.Id);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesMembershipVersion);
        }

        [TestMethod]
        public void SetImageLockedAsync_BumpsImagesMembershipVersion_OnChange()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesMembershipVersion;
            var result = _engine.SetImageLockedAsync(_state, _host, _mapId, img.Id, true);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsGreaterThan(before, Map.ImagesMembershipVersion);
        }

        [TestMethod]
        public void UpdateImageTransformAsync_DoesNotBumpImagesMembershipVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesMembershipVersion;
            var result = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id, 1, 2, 3, 4, 10, 0.5);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(before, Map.ImagesMembershipVersion);
        }

        [TestMethod]
        public void SetImageHiddenAsync_DoesNotBumpImagesMembershipVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesMembershipVersion;
            var result = _engine.SetImageHiddenAsync(_state, _host, _mapId, img.Id, true);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(before, Map.ImagesMembershipVersion);
        }

        [TestMethod]
        public void UpdateImageShareTokenAsync_DoesNotBumpImagesMembershipVersion()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var before = Map.ImagesMembershipVersion;
            var result = _engine.UpdateImageShareTokenAsync(_state, _host, _mapId, img.Id, Guid.NewGuid());
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(before, Map.ImagesMembershipVersion);
        }
    }
}
