using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Library.Vtf;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Services.Library.Vtf
{
    /// <summary>
    /// Covers the client-side export path's server contribution: building the
    /// JSON shards a VTF archive needs without ever reading image bytes. The
    /// JS-side packer (dndMapperVtfPackager.js) is responsible for assembling
    /// the ZIP and embedding image bytes from IndexedDB; these tests stop at
    /// the server boundary.
    /// </summary>
    [TestClass]
    public class ClientPayloadTests
    {
        [TestMethod]
        public async Task BuildExportPayloadAsync_UnknownSlotId_ReturnsError()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var r = await library.BuildExportPayloadAsync("does-not-exist");
                Assert.IsTrue(r.IsFailure);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task BuildExportPayloadAsync_EmptySlot_ProducesManifestGlobalAndExtension()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var create = await library.CreateSlotAsync("Empty");
                Assert.IsTrue(create.TryGetSuccess(out var slotId));

                var r = await library.BuildExportPayloadAsync(slotId);
                Assert.IsTrue(r.TryGetSuccess(out var payload));
                Assert.AreEqual("Empty", payload.SlotName);
                Assert.AreEqual("Empty.vtf", payload.FileName);

                var paths = payload.Entries.Select(e => e.Path).ToList();
                CollectionAssert.Contains(paths, "manifest.json");
                CollectionAssert.Contains(paths, "global_state.json");
                Assert.IsTrue(paths.Any(p => p.StartsWith("extensions/")),
                    "An extension entry must always be present.");
                // Newly-created slot has no maps / no sheets, so no scenes /
                // entities yet. Image refs match.
                Assert.AreEqual(0, paths.Count(p => p.StartsWith("scenes/")));
                Assert.AreEqual(0, paths.Count(p => p.StartsWith("entities/")));
                Assert.AreEqual(0, payload.Images.Count);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task BuildExportPayloadAsync_DoesNotReadBlobStore()
        {
            // The key behavior the new client-side packer relies on: image
            // bytes never cross the SignalR boundary. This test asserts the
            // server-side build never invokes BlobGetSingleAsync — verified
            // by tracking calls on the FakeIndexedDbService.
            var (db, library) = await AttachFreshAsync();
            try
            {
                var create = await library.CreateSlotAsync("Slot");
                Assert.IsTrue(create.TryGetSuccess(out var slotId));

                var blobReadsBefore = db.BlobReadCalls;
                var r = await library.BuildExportPayloadAsync(slotId);
                Assert.IsTrue(r.IsSuccess);
                Assert.AreEqual(blobReadsBefore, db.BlobReadCalls,
                    "BuildExportPayloadAsync must not read image blobs server-side.");
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task BuildExportPayloadAsync_JsonEntriesParseAsJsonObjects()
        {
            // Every entry's Content must round-trip through System.Text.Json
            // as a JSON object. This guards against a regression where the
            // JSON-serialization step silently produces a string with non-JSON
            // content (e.g. the result of ToString()).
            var (_, library) = await AttachFreshAsync();
            try
            {
                var create = await library.CreateSlotAsync("Slot");
                Assert.IsTrue(create.TryGetSuccess(out var slotId));
                var r = await library.BuildExportPayloadAsync(slotId);
                Assert.IsTrue(r.TryGetSuccess(out var payload));

                foreach (var entry in payload.Entries)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(entry.Content);
                    Assert.AreEqual(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind,
                        $"Entry {entry.Path} must serialize to a JSON object.");
                }
            }
            finally { await library.DisposeAsync(); }
        }

        private static async Task<(FakeIndexedDbService Db, DndMapperLibraryService Library)> AttachFreshAsync()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            var attach = await library.AttachAsync(state, host);
            Assert.IsTrue(attach.IsSuccess);
            return (db, library);
        }
    }
}
