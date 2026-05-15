using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Services.Library;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    [TestClass]
    public class DndMapperLibrarySchemaTests
    {
        [TestMethod]
        public void Schema_DeclaresBothStores()
        {
            var schema = DndMapperLibrarySchema.Create();

            Assert.IsNotNull(schema.Stores, "Schema must declare its stores so JS can reconcile synchronously.");
            Assert.AreEqual(2, schema.Stores!.Count);

            var library = schema.Stores.Single(s => s.Name == DndMapperLibrarySchema.LibraryStore);
            Assert.AreEqual(DeclaredStoreKind.Json, library.Kind);

            var images = schema.Stores.Single(s => s.Name == DndMapperLibrarySchema.ImagesStore);
            Assert.AreEqual(DeclaredStoreKind.Blob, images.Kind);
        }

        [TestMethod]
        public void Schema_VersionMatchesCurrent()
        {
            var schema = DndMapperLibrarySchema.Create();

            Assert.AreEqual(DndMapperLibrarySchema.CurrentVersion, schema.Version);
            Assert.AreEqual(DndMapperLibrarySchema.DatabaseName, schema.Name);
        }
    }
}
