using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class ImageVerbsTests
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

        private static MapImage SeedImage(long bytes = 100, string contentType = "image/png")
            => new()
            {
                Id = Guid.NewGuid(),
                ContentType = contentType,
                ShareToken = Guid.NewGuid(),
                Width = 10,
                Height = 10,
                Opacity = 1.0,
                ByteSize = bytes,
            };

        // ── AddImageAsync ─────────────────────────────────────────────────────────

        [TestMethod]
        public void AddImageAsync_HostCaller_AppendsAndIncrementsBytes()
        {
            var img = SeedImage(bytes: 100);

            var result = _engine.AddImageAsync(_state, _host, _mapId, img);

            Assert.IsTrue(result.TryGetSuccess(out var added));
            Assert.AreEqual(1, _state.Maps[0].Images.Count);
            Assert.AreEqual(0, added.LayerOrder);
            Assert.AreEqual(100, _state.BytesUsed);
        }

        [TestMethod]
        public void AddImageAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            var img = SeedImage();

            var result = _engine.AddImageAsync(_state, player, _mapId, img);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.Maps[0].Images.Count);
        }

        [TestMethod]
        public void AddImageAsync_UnknownMapId_ReturnsError()
        {
            var img = SeedImage();
            var result = _engine.AddImageAsync(_state, _host, Guid.NewGuid(), img);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.BytesUsed);
        }

        [TestMethod]
        public void AddImageAsync_MultipleImages_LayerOrderAscends()
        {
            var a = SeedImage(50);
            var b = SeedImage(50);
            var c = SeedImage(50);
            _engine.AddImageAsync(_state, _host, _mapId, a);
            _engine.AddImageAsync(_state, _host, _mapId, b);
            _engine.AddImageAsync(_state, _host, _mapId, c);

            Assert.AreEqual(0, a.LayerOrder);
            Assert.AreEqual(1, b.LayerOrder);
            Assert.AreEqual(2, c.LayerOrder);
            Assert.AreEqual(150, _state.BytesUsed);
        }

        [TestMethod]
        public void AddImageAsync_PerFileCapExceeded_ReturnsError()
        {
            var img = SeedImage(bytes: (5L * 1024 * 1024) + 1);
            var result = _engine.AddImageAsync(_state, _host, _mapId, img);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.BytesUsed);
        }

        [TestMethod]
        public void AddImageAsync_RoomCapExceeded_ReturnsError()
        {
            // Each image is under the 5 MB per-file cap; combined they're under
            // the 10 MB room cap; the third pushes past 10 MB and must be rejected.
            var a = SeedImage(bytes: 4L * 1024 * 1024);
            var b = SeedImage(bytes: 4L * 1024 * 1024);
            Assert.IsTrue(_engine.AddImageAsync(_state, _host, _mapId, a).IsSuccess);
            Assert.IsTrue(_engine.AddImageAsync(_state, _host, _mapId, b).IsSuccess);

            var tooBig = SeedImage(bytes: 3L * 1024 * 1024);
            var result = _engine.AddImageAsync(_state, _host, _mapId, tooBig);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(8L * 1024 * 1024, _state.BytesUsed);
        }

        [TestMethod]
        public void AddImageAsync_UnsupportedContentType_ReturnsError()
        {
            var img = SeedImage(contentType: "image/gif");
            var result = _engine.AddImageAsync(_state, _host, _mapId, img);
            Assert.IsTrue(result.IsFailure);
        }

        // ── UpdateImageTransformAsync ─────────────────────────────────────────────

        [TestMethod]
        public void UpdateImageTransformAsync_HostCaller_MutatesInPlace()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);

            var result = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id,
                x: 1, y: 2, width: 30, height: 40, rotation: 90, opacity: 0.5);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(30, img.Width);
            Assert.AreEqual(40, img.Height);
            Assert.AreEqual(0.5, img.Opacity);
            Assert.AreEqual(90, img.Rotation);
        }

        [TestMethod]
        public void UpdateImageTransformAsync_NonPositiveSize_ReturnsError()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);

            var negW = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id, 0, 0, -1, 10, 0, 1.0);
            var zeroH = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id, 0, 0, 10, 0, 0, 1.0);

            Assert.IsTrue(negW.IsFailure);
            Assert.IsTrue(zeroH.IsFailure);
        }

        [TestMethod]
        public void UpdateImageTransformAsync_OpacityOutOfRange_ReturnsError()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);

            var lo = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id, 0, 0, 10, 10, 0, -0.1);
            var hi = _engine.UpdateImageTransformAsync(_state, _host, _mapId, img.Id, 0, 0, 10, 10, 0, 1.1);

            Assert.IsTrue(lo.IsFailure);
            Assert.IsTrue(hi.IsFailure);
        }

        [TestMethod]
        public void UpdateImageTransformAsync_NonHostCaller_ReturnsError()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.UpdateImageTransformAsync(_state, player, _mapId, img.Id, 0, 0, 10, 10, 0, 1.0);
            Assert.IsTrue(result.IsFailure);
        }

        // ── ReorderImageLayerAsync ────────────────────────────────────────────────

        [TestMethod]
        public void ReorderImageLayerAsync_HostCaller_RenumbersLayerOrder()
        {
            var a = SeedImage(); _engine.AddImageAsync(_state, _host, _mapId, a);
            var b = SeedImage(); _engine.AddImageAsync(_state, _host, _mapId, b);
            var c = SeedImage(); _engine.AddImageAsync(_state, _host, _mapId, c);

            // Move c (index 2) to index 0.
            var result = _engine.ReorderImageLayerAsync(_state, _host, _mapId, c.Id, 0);

            Assert.IsTrue(result.IsSuccess);
            var images = _state.Maps[0].Images;
            Assert.AreEqual(c.Id, images[0].Id);
            Assert.AreEqual(0, images[0].LayerOrder);
            Assert.AreEqual(1, images[1].LayerOrder);
            Assert.AreEqual(2, images[2].LayerOrder);
        }

        [TestMethod]
        public void ReorderImageLayerAsync_NewOrderOutOfRange_ReturnsError()
        {
            var a = SeedImage(); _engine.AddImageAsync(_state, _host, _mapId, a);

            var result = _engine.ReorderImageLayerAsync(_state, _host, _mapId, a.Id, 5);
            Assert.IsTrue(result.IsFailure);
        }

        // ── RemoveImageAsync ──────────────────────────────────────────────────────

        [TestMethod]
        public void RemoveImageAsync_HostCaller_RemovesFromListAndDecrementsBytes()
        {
            var a = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, a);
            var b = SeedImage(75); _engine.AddImageAsync(_state, _host, _mapId, b);
            Assert.AreEqual(125, _state.BytesUsed);

            var result = _engine.RemoveImageAsync(_state, _host, _mapId, a.Id);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, _state.Maps[0].Images.Count);
            Assert.AreEqual(75, _state.BytesUsed);
            // Remaining image's LayerOrder compacted to 0.
            Assert.AreEqual(0, _state.Maps[0].Images[0].LayerOrder);
        }

        [TestMethod]
        public void RemoveImageAsync_UnknownImageId_ReturnsError()
        {
            var result = _engine.RemoveImageAsync(_state, _host, _mapId, Guid.NewGuid());
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void RemoveImageAsync_NullsShareTokenOnRemovedImage()
        {
            // The host's library disposes the cached IBlobShare on remove, but
            // the MapImage instance can outlive removal (UI alias, snapshot
            // mid-write). Nulling the ShareToken in the same Execute prevents
            // a dangling capability token from leaking into any later read.
            var img = SeedImage();
            Assert.IsTrue(_engine.AddImageAsync(_state, _host, _mapId, img).IsSuccess);
            Assert.IsNotNull(img.ShareToken);

            Assert.IsTrue(_engine.RemoveImageAsync(_state, _host, _mapId, img.Id).IsSuccess);

            Assert.IsNull(img.ShareToken);
        }

        // ── DeleteMapAsync image cascade ──────────────────────────────────────────

        [TestMethod]
        public void DeleteMapAsync_CascadesAllImageDeletes()
        {
            var a = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, a);
            var b = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, b);
            var c = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, c);
            Assert.AreEqual(150, _state.BytesUsed);

            var result = _engine.DeleteMapAsync(_state, _host, _mapId);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, _state.Maps.Count);
            Assert.AreEqual(0, _state.BytesUsed);
        }

        // ── SetImageLockedAsync ───────────────────────────────────────────────────

        [TestMethod]
        public void SetImageLockedAsync_HostCaller_TogglesLocked()
        {
            var img = SeedImage();
            Assert.IsTrue(_engine.AddImageAsync(_state, _host, _mapId, img).IsSuccess);

            var lockResult = _engine.SetImageLockedAsync(_state, _host, _mapId, img.Id, true);
            Assert.IsTrue(lockResult.IsSuccess);
            Assert.IsTrue(_state.Maps[0].Images.Single().Locked);

            var unlockResult = _engine.SetImageLockedAsync(_state, _host, _mapId, img.Id, false);
            Assert.IsTrue(unlockResult.IsSuccess);
            Assert.IsFalse(_state.Maps[0].Images.Single().Locked);
        }

        [TestMethod]
        public void SetImageLockedAsync_NonHost_ReturnsError()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            var player = EngineTestFactory.RegisterPlayer(_state);

            var result = _engine.SetImageLockedAsync(_state, player, _mapId, img.Id, true);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void SetImageLockedAsync_UnknownImage_ReturnsError()
        {
            var result = _engine.SetImageLockedAsync(_state, _host, _mapId, Guid.NewGuid(), true);
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void UpdateImageTransformAsync_LockedImage_ReturnsError()
        {
            var img = SeedImage();
            _engine.AddImageAsync(_state, _host, _mapId, img);
            _engine.SetImageLockedAsync(_state, _host, _mapId, img.Id, true);

            var result = _engine.UpdateImageTransformAsync(
                _state, _host, _mapId, img.Id, 5, 5, 20, 20, 0, 1.0);
            Assert.IsTrue(result.IsFailure);

            // Unchanged
            var fresh = _state.Maps[0].Images.Single();
            Assert.AreEqual(0, fresh.X);
            Assert.AreEqual(10, fresh.Width);
        }

        [TestMethod]
        public void ReorderImageLayerAsync_LockedImage_ReturnsError()
        {
            var a = SeedImage(); _engine.AddImageAsync(_state, _host, _mapId, a);
            var b = SeedImage(); _engine.AddImageAsync(_state, _host, _mapId, b);
            _engine.SetImageLockedAsync(_state, _host, _mapId, a.Id, true);

            var result = _engine.ReorderImageLayerAsync(_state, _host, _mapId, a.Id, 1);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.Maps[0].Images[0].LayerOrder);
            Assert.AreEqual(a.Id, _state.Maps[0].Images[0].Id);
        }
    }
}
