using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    /// <summary>
    /// Exercises <see cref="DndMapperLibraryService.AttachAsync"/>'s v2→v3
    /// migration and the missing-store recovery path via a fake
    /// <see cref="KnockBox.Core.Services.Storage.IndexedDb.IIndexedDbService"/>.
    /// </summary>
    [TestClass]
    public class DndMapperLibraryServiceMigrationTests
    {
        [TestMethod]
        public async Task AttachAsync_NoLegacyData_WritesEmptySlotsIndex()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullLogger<DndMapperLibraryService>.Instance);

            var attach = await library.AttachAsync(state, host);

            Assert.IsTrue(attach.IsSuccess, "AttachAsync should succeed.");
            Assert.IsFalse(library.HasExistingLibrary, "No legacy data → no existing library.");

            var listed = await library.ListSlotsAsync();
            Assert.IsTrue(listed.TryGetSuccess(out var slots));
            Assert.AreEqual(0, slots.Count, "Empty slots index after fresh attach.");

            Assert.IsTrue(db.JsonStores.TryGetValue(DndMapperLibrarySchema.SlotsIndexStore, out var idxStore));
            Assert.IsTrue(idxStore!.ContainsKey(DndMapperLibrarySchema.SlotsIndexKey),
                "Migration must persist a slots-index record even when empty.");
        }

        [TestMethod]
        public async Task AttachAsync_WithLegacySnapshot_MigratesToAutoSlot()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            db.JsonStores[DndMapperLibrarySchema.LibraryStore] = new()
            {
                [DndMapperLibrarySchema.LegacySingletonKey] = new LibrarySnapshot(),
            };

            await using var library = new DndMapperLibraryService(db, engine, NullLogger<DndMapperLibraryService>.Instance);
            var attach = await library.AttachAsync(state, host);

            Assert.IsTrue(attach.IsSuccess);
            Assert.IsTrue(library.HasExistingLibrary, "Auto Save slot present → existing library.");

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            Assert.IsFalse(libStore.ContainsKey(DndMapperLibrarySchema.LegacySingletonKey),
                "Legacy singleton row should be removed after a successful migration.");
            Assert.IsTrue(libStore.ContainsKey(DndMapperLibrarySchema.AutoSlotId),
                "Legacy snapshot should be lifted into the Auto Save slot id.");

            var listed = await library.ListSlotsAsync();
            Assert.IsTrue(listed.TryGetSuccess(out var slots));
            Assert.AreEqual(1, slots.Count);
            Assert.AreEqual(DndMapperLibrarySchema.AutoSlotId, slots[0].Id);
            Assert.AreEqual(SlotKind.Auto, slots[0].Kind);
        }

        [TestMethod]
        public async Task AttachAsync_AlreadyMigrated_IsIdempotent()
        {
            var (engine1, state1, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            db.JsonStores[DndMapperLibrarySchema.LibraryStore] = new()
            {
                [DndMapperLibrarySchema.LegacySingletonKey] = new LibrarySnapshot(),
            };

            var library1 = new DndMapperLibraryService(db, engine1, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library1.AttachAsync(state1, host)).IsSuccess);
            await library1.DisposeAsync();

            Assert.AreEqual(1, ReadSlotCount(db));

            var (engine2, state2, _, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library2.AttachAsync(state2, host)).IsSuccess);

            Assert.AreEqual(1, ReadSlotCount(db), "Re-attach must not duplicate the Auto Save entry.");
        }

        [TestMethod]
        public async Task AttachAsync_WhenStoresMissing_RecoversByRecreating()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService { MissingStoresOnNextOpen = true };

            await using var library = new DndMapperLibraryService(db, engine, NullLogger<DndMapperLibraryService>.Instance);

            var attach = await library.AttachAsync(state, host);

            Assert.IsTrue(attach.IsSuccess, "Recovery should succeed on second open.");
            Assert.AreEqual(1, db.DeleteDatabaseCallCount, "Stale DB should have been deleted once.");
            Assert.AreEqual(2, db.OpenCallCount, "Should have opened, deleted, then reopened.");
        }

        private static int ReadSlotCount(FakeIndexedDbService db)
        {
            if (!db.JsonStores.TryGetValue(DndMapperLibrarySchema.SlotsIndexStore, out var idxStore))
                return 0;
            if (!idxStore.TryGetValue(DndMapperLibrarySchema.SlotsIndexKey, out var raw))
                return 0;
            return ((SlotsIndex)raw).Slots.Count;
        }
    }
}
