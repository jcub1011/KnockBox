using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    /// <summary>
    /// Covers the Detach / Dispose split — specifically that a host page's
    /// teardown call leaves the scoped service reusable, and that
    /// <see cref="DndMapperLibraryService.DisposeAsync"/> still finalizes.
    /// AttachAsync is not exercised here (it requires a real or rich-fake
    /// <c>IIndexedDatabase</c>); the schema-upgrade behavior lives in
    /// <see cref="DndMapperLibrarySchemaTests"/>.
    /// </summary>
    [TestClass]
    public class DndMapperLibraryServiceLifecycleTests
    {
        [TestMethod]
        public async Task DetachAsync_WhenNeverAttached_IsNoOp()
        {
            var (_, _, _, _) = EngineTestFactory.Build();
            var library = BuildLibrary();

            await library.DetachAsync();
            await library.DetachAsync();
        }

        [TestMethod]
        public async Task DisposeAsync_WhenNeverAttached_DoesNotThrow()
        {
            var library = BuildLibrary();

            await library.DisposeAsync();
        }

        [TestMethod]
        public async Task DisposeAsync_AfterDetach_DoesNotThrow()
        {
            var library = BuildLibrary();

            await library.DetachAsync();
            await library.DisposeAsync();
        }

        [TestMethod]
        public async Task DisposeAsync_IsIdempotent()
        {
            var library = BuildLibrary();

            await library.DisposeAsync();
            await library.DisposeAsync();
        }

        [TestMethod]
        public async Task TryGetLocalObjectUrlAsync_WhenBlobNotCached_ReturnsNull()
        {
            // The player-circuit shape: this circuit didn't upload the image,
            // so the blob cache is empty. MapCanvas relies on null here to
            // fall through to the /blob-share share URL.
            var library = BuildLibrary();

            var url = await library.TryGetLocalObjectUrlAsync(Guid.NewGuid());

            Assert.IsNull(url);
        }

        [TestMethod]
        public async Task TryGetLocalObjectUrlAsync_AfterDispose_ReturnsNullWithoutThrowing()
        {
            var library = BuildLibrary();
            await library.DisposeAsync();

            var url = await library.TryGetLocalObjectUrlAsync(Guid.NewGuid());

            Assert.IsNull(url);
        }

        // The library service wraps Engine.DeleteMapAsync so blob-share handles for
        // every image on the deleted map get disposed (which revokes the registry
        // entry + evicts the byte cache). Validate the precondition path here —
        // the "actually disposes shares" assertion needs an attached IndexedDB
        // fake which the rest of this file deliberately avoids.
        [TestMethod]
        public async Task DeleteMapAsync_WhenNotAttached_ReturnsError()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var create = engine.CreateMapAsync(state, host, "M");
            Assert.IsTrue(create.TryGetSuccess(out var mapId));

            var library = BuildLibrary();

            var result = await library.DeleteMapAsync(state, host, mapId);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(state.Maps.Any(m => m.Id == mapId),
                "Engine must not be invoked when library is unattached — map should still be present.");
        }

        private static DndMapperLibraryService BuildLibrary()
        {
            var (engine, _, _, _) = EngineTestFactory.Build();
            var indexedDb = new Mock<IIndexedDbService>(MockBehavior.Strict).Object;
            return new DndMapperLibraryService(
                indexedDb, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
        }
    }
}
