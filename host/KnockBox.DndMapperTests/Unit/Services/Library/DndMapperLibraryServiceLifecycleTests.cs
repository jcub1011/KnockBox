using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Services.Library;
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

        private static DndMapperLibraryService BuildLibrary()
        {
            var (engine, _, _, _) = EngineTestFactory.Build();
            var indexedDb = new Mock<IIndexedDbService>(MockBehavior.Strict).Object;
            return new DndMapperLibraryService(
                indexedDb, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
        }
    }
}
