using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.Logic.Games
{
    [TestClass]
    public class SaveImageAsyncTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;
        private InMemoryPluginStorage _storage = default!;
        private Guid _mapId;

        // PNG magic header.
        private static readonly byte[] PngMagic =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        [TestInitialize]
        public void Setup()
        {
            _storage = new InMemoryPluginStorage();
            (_engine, _state, _host, _) = EngineTestFactory.Build(_storage);
            var create = _engine.CreateMapAsync(_state, _host, "M");
            Assert.IsTrue(create.TryGetSuccess(out _mapId));
        }

        private static MemoryStream MakePng(int totalBytes)
        {
            var bytes = new byte[totalBytes];
            Array.Copy(PngMagic, bytes, PngMagic.Length);
            return new MemoryStream(bytes, writable: false);
        }

        [TestMethod]
        public async Task SaveImageAsync_HostHappyPath_PersistsAndReturnsMapImage()
        {
            using var stream = MakePng(2048);

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, declaredLength: 2048);

            Assert.IsTrue(result.TryGetSuccess(out var img));
            Assert.AreEqual(2048, img.ByteSize);
            Assert.IsTrue(img.RelativePath.StartsWith($"{_state.SessionId}/images/"));
            Assert.IsTrue(img.RelativePath.EndsWith(".png"));
            Assert.IsTrue(_storage.Exists(img.RelativePath));
            Assert.AreEqual(1, _state.Maps[0].Images.Count);
            Assert.AreEqual(2048, _state.BytesUsed);
        }

        [TestMethod]
        public async Task SaveImageAsync_NonHostCaller_ReturnsError()
        {
            var player = EngineTestFactory.RegisterPlayer(_state);
            using var stream = MakePng(100);

            var result = await _engine.SaveImageAsync(_state, player, _mapId, stream, 100);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _storage.Files.Count);
        }

        [TestMethod]
        public async Task SaveImageAsync_DeclaredLengthOverPerFileCap_ReturnsErrorBeforeStreamRead()
        {
            using var stream = MakePng(100);
            long sixMb = 6L * 1024 * 1024;

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, declaredLength: sixMb);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, stream.Position, "Stream should not be read when declared length exceeds cap.");
            Assert.AreEqual(0, _storage.Files.Count);
        }

        [TestMethod]
        public async Task SaveImageAsync_OverRoomCap_ReturnsError()
        {
            // Pre-load BytesUsed to 9 MB by adding a fake image with that ByteSize.
            var prefill = new KnockBox.DndMapper.Services.State.Games.Data.MapImage
            {
                Id = Guid.NewGuid(),
                RelativePath = $"{_state.SessionId}/images/prefill.png",
                ByteSize = 9L * 1024 * 1024,
                Width = 1, Height = 1, Opacity = 1.0,
            };
            _engine.AddImageAsync(_state, _host, _mapId, prefill);
            Assert.AreEqual(9L * 1024 * 1024, _state.BytesUsed);

            using var stream = MakePng(2 * 1024 * 1024);

            int beforeFiles = _storage.Files.Count;
            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, 2L * 1024 * 1024);

            Assert.IsTrue(result.IsFailure);
            // No new file should have been written.
            Assert.AreEqual(beforeFiles, _storage.Files.Count);
        }

        [TestMethod]
        public async Task SaveImageAsync_BadMime_ReturnsError()
        {
            // Random bytes — no magic.
            var bytes = new byte[64];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i + 1);
            using var stream = new MemoryStream(bytes);

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, bytes.Length);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _storage.Files.Count);
        }

        [TestMethod]
        public async Task SaveImageAsync_SvgRejected_ReturnsError()
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes("<svg xmlns=\"\"></svg>          ");
            using var stream = new MemoryStream(bytes);

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, bytes.Length);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task SaveImageAsync_StreamLongerThanDeclared_DeletesPartialFileAndReturnsError()
        {
            // Stream contains 6 MB of valid PNG-prefixed bytes; we declare only 4 MB.
            using var stream = MakePng(6 * 1024 * 1024);

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, declaredLength: 4L * 1024 * 1024);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.Maps[0].Images.Count);
            Assert.AreEqual(0, _state.BytesUsed);
            // Partial file should have been cleaned up.
            Assert.AreEqual(0, _storage.Files.Count);
        }

        [TestMethod]
        public async Task SaveImageAsync_StorageWriteFailure_RollsBackAndReturnsError()
        {
            _storage.OpenWriteOverride = _ => throw new IOException("disk full");
            using var stream = MakePng(1024);

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, 1024);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.Maps[0].Images.Count);
            Assert.AreEqual(0, _state.BytesUsed);
        }

        [TestMethod]
        public async Task SaveImageAsync_UnknownMapId_ReturnsError()
        {
            using var stream = MakePng(1024);

            var result = await _engine.SaveImageAsync(_state, _host, Guid.NewGuid(), stream, 1024);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _storage.Files.Count);
        }

        [TestMethod]
        public async Task SaveImageAsync_AddImageRejection_DeletesDiskFile()
        {
            // Drive the TOCTOU window between SaveImageAsync's pre-flight read lock and
            // its post-write AddImageAsync call: a stream whose final ReadAsync deletes
            // the map, so that AddImageAsync rejects with "Unknown map id." after the
            // file has already been written. The rollback path in DndMapperGameEngine
            // then deletes the partial file.
            using var stream = new DeleteMapOnEofStream(MakePng(2048).ToArray(), () =>
            {
                var del = _engine.DeleteMapAsync(_state, _host, _mapId);
                Assert.IsTrue(del.IsSuccess, "Map delete inside the stream tap must succeed.");
            });

            int beforeFiles = _storage.Files.Count;
            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, declaredLength: 2048);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(0, _state.Maps.Count, "Map should have been deleted by the stream tap.");
            Assert.AreEqual(0, _state.BytesUsed);
            // Partial file written before AddImageAsync rejected must be cleaned up.
            Assert.AreEqual(beforeFiles, _storage.Files.Count);
        }

        /// <summary>
        /// Stream that returns the supplied bytes and runs <paramref name="onEof"/> exactly
        /// once when the consumer hits the end. Used to drive a deterministic race between
        /// SaveImageAsync's stream copy and a concurrent map deletion.
        /// </summary>
        private sealed class DeleteMapOnEofStream : MemoryStream
        {
            private readonly Action _onEof;
            private bool _fired;

            public DeleteMapOnEofStream(byte[] buffer, Action onEof) : base(buffer, writable: false)
            {
                _onEof = onEof;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                int n = await base.ReadAsync(buffer, cancellationToken);
                if (n == 0 && !_fired)
                {
                    _fired = true;
                    _onEof();
                }
                return n;
            }
        }

        [TestMethod]
        public async Task SaveImageAsync_DeclaredLengthZero_ReturnsError()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());

            var result = await _engine.SaveImageAsync(_state, _host, _mapId, stream, 0);

            Assert.IsTrue(result.IsFailure);
        }
    }
}
