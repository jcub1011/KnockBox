using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class ImageVerbsTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private InMemoryPluginStorage _storage = default!;
        private Guid _mapId;

        [TestInitialize]
        public void Setup()
        {
            _storage = new InMemoryPluginStorage();
            (_engine, _state, _host, _) = EngineTestFactory.Build(_storage);
            var create = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(create.TryGetSuccess(out _mapId));
        }

        private MapImage SeedImage(long bytes = 100, string ext = "png")
        {
            var img = new MapImage
            {
                Id = Guid.NewGuid(),
                RelativePath = $"{_state.SessionId}/images/{Guid.NewGuid()}.{ext}",
                Width = 10,
                Height = 10,
                Opacity = 1.0,
                ByteSize = bytes,
            };
            _storage.Seed(img.RelativePath, new byte[bytes]);
            return img;
        }

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
        public void RemoveImageAsync_HostCaller_RemovesFromListDeletesFromStorageDecrementsBytes()
        {
            var a = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, a);
            var b = SeedImage(75); _engine.AddImageAsync(_state, _host, _mapId, b);
            Assert.AreEqual(125, _state.BytesUsed);
            Assert.IsTrue(_storage.Exists(a.RelativePath));

            var result = _engine.RemoveImageAsync(_state, _host, _mapId, a.Id);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, _state.Maps[0].Images.Count);
            Assert.AreEqual(75, _state.BytesUsed);
            Assert.IsFalse(_storage.Exists(a.RelativePath));
            // Remaining image's LayerOrder compacted to 0.
            Assert.AreEqual(0, _state.Maps[0].Images[0].LayerOrder);
        }

        [TestMethod]
        public void RemoveImageAsync_StorageDeleteFails_ReturnsSuccessAndLogs()
        {
            // Rebuild the engine with a capturing logger so we can assert the warning.
            var storage = new InMemoryPluginStorage();
            var logger = new CapturingLogger<DndMapperGameEngine>();
            var (engine, state, host, _) = EngineTestFactory.Build(storage, logger);
            var createResult = engine.CreateMapAsync(state, host, "M");
            Assert.IsTrue(createResult.TryGetSuccess(out var mapId));

            var a = new MapImage
            {
                Id = Guid.NewGuid(),
                RelativePath = $"{state.SessionId}/images/{Guid.NewGuid()}.png",
                Width = 10, Height = 10, Opacity = 1.0,
                ByteSize = 50,
            };
            storage.Seed(a.RelativePath, new byte[50]);
            engine.AddImageAsync(state, host, mapId, a);

            var boom = new IOException("boom");
            storage.DeleteOverride = _ => throw boom;

            var result = engine.RemoveImageAsync(state, host, mapId, a.Id);

            // Verb succeeds even though disk delete failed; in-memory state is the source of truth.
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, state.Maps[0].Images.Count);
            Assert.AreEqual(0, state.BytesUsed);

            var warning = logger.Warnings.FirstOrDefault(w => w.Message.Contains("Failed to delete image file"));
            Assert.IsNotNull(warning.Message, "Expected a warning log for the failed disk delete.");
            Assert.AreSame(boom, warning.Exception);
        }

        [TestMethod]
        public void RemoveImageAsync_UnknownImageId_ReturnsError()
        {
            var result = _engine.RemoveImageAsync(_state, _host, _mapId, Guid.NewGuid());
            Assert.IsTrue(result.IsFailure);
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
            Assert.IsFalse(_storage.Exists(a.RelativePath));
            Assert.IsFalse(_storage.Exists(b.RelativePath));
            Assert.IsFalse(_storage.Exists(c.RelativePath));
        }

        // ── Session-end cleanup ───────────────────────────────────────────────────

        [TestMethod]
        public void Dispose_FiresStorageCleanup_RemovesAllSessionFiles()
        {
            var a = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, a);
            var b = SeedImage(50); _engine.AddImageAsync(_state, _host, _mapId, b);

            // Add an unrelated file to ensure the cleanup is scoped.
            _storage.Seed("other-session/images/foo.png", new byte[10]);

            _state.Dispose();

            Assert.IsFalse(_storage.Exists(a.RelativePath));
            Assert.IsFalse(_storage.Exists(b.RelativePath));
            Assert.IsTrue(_storage.Exists("other-session/images/foo.png"),
                "Cleanup should be scoped to the session prefix.");
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
