using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    /// <summary>
    /// Covers the manual slot APIs (List/Create/Save/Delete/Rename/Load).
    /// </summary>
    [TestClass]
    public class DndMapperLibrarySlotTests
    {
        [TestMethod]
        public async Task CreateSlotAsync_RejectsEmptyName()
        {
            var (db, library) = await AttachFreshAsync();
            try
            {
                var r1 = await library.CreateSlotAsync("");
                var r2 = await library.CreateSlotAsync("   ");
                Assert.IsFalse(r1.IsSuccess);
                Assert.IsFalse(r2.IsSuccess);
            }
            finally { await library.DisposeAsync(); _ = db; }
        }

        [TestMethod]
        public async Task CreateSlotAsync_RejectsTooLongName()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var r = await library.CreateSlotAsync(new string('a', 61));
                Assert.IsFalse(r.IsSuccess);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task CreateSlotAsync_RejectsDuplicateManualName()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                Assert.IsTrue((await library.CreateSlotAsync("Campaign A")).IsSuccess);
                var dup = await library.CreateSlotAsync("campaign a");
                Assert.IsFalse(dup.IsSuccess, "Duplicate names should be case-insensitive.");
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task CreateSlotAsync_WritesEntryAndSnapshot()
        {
            var (db, library) = await AttachFreshAsync();
            try
            {
                var r = await library.CreateSlotAsync("Campaign A");
                Assert.IsTrue(r.TryGetSuccess(out var slotId));

                var listed = await library.ListSlotsAsync();
                Assert.IsTrue(listed.TryGetSuccess(out var slots));
                Assert.Contains(s => s.Id == slotId && s.Name == "Campaign A" && s.Kind == SlotKind.Manual, slots);

                Assert.IsTrue(db.JsonStores[DndMapperLibrarySchema.LibraryStore].ContainsKey(slotId),
                    "Snapshot must be written under the slot's id.");
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task SaveToSlotAsync_RejectsAutoSlot()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var r = await library.SaveToSlotAsync(DndMapperLibrarySchema.AutoSlotId);
                Assert.IsFalse(r.IsSuccess);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task SaveToSlotAsync_UpdatesTimestamp()
        {
            var (db, library) = await AttachFreshAsync();
            try
            {
                var create = await library.CreateSlotAsync("S");
                Assert.IsTrue(create.TryGetSuccess(out var slotId));

                var idx = (SlotsIndex)db.JsonStores[DndMapperLibrarySchema.SlotsIndexStore][DndMapperLibrarySchema.SlotsIndexKey];
                var before = idx.Slots.Single(s => s.Id == slotId).UpdatedUtc;

                await Task.Delay(5);
                Assert.IsTrue((await library.SaveToSlotAsync(slotId)).IsSuccess);

                idx = (SlotsIndex)db.JsonStores[DndMapperLibrarySchema.SlotsIndexStore][DndMapperLibrarySchema.SlotsIndexKey];
                var after = idx.Slots.Single(s => s.Id == slotId).UpdatedUtc;
                Assert.IsGreaterThan(before, after, "SaveToSlotAsync should bump UpdatedUtc.");
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task DeleteSlotAsync_RejectsAutoSlot()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var r = await library.DeleteSlotAsync(DndMapperLibrarySchema.AutoSlotId);
                Assert.IsFalse(r.IsSuccess);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task DeleteSlotAsync_RemovesEntryAndSnapshot()
        {
            var (db, library) = await AttachFreshAsync();
            try
            {
                var create = await library.CreateSlotAsync("S");
                Assert.IsTrue(create.TryGetSuccess(out var slotId));

                Assert.IsTrue((await library.DeleteSlotAsync(slotId)).IsSuccess);

                Assert.IsFalse(db.JsonStores[DndMapperLibrarySchema.LibraryStore].ContainsKey(slotId));
                var listed = await library.ListSlotsAsync();
                Assert.IsTrue(listed.TryGetSuccess(out var slots));
                Assert.DoesNotContain(s => s.Id == slotId, slots);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task RenameSlotAsync_RejectsAutoSlot()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var r = await library.RenameSlotAsync(DndMapperLibrarySchema.AutoSlotId, "Nope");
                Assert.IsFalse(r.IsSuccess);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task RenameSlotAsync_RejectsDuplicateManualName()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                Assert.IsTrue((await library.CreateSlotAsync("Alpha")).IsSuccess);
                var second = await library.CreateSlotAsync("Beta");
                Assert.IsTrue(second.TryGetSuccess(out var betaId));

                var r = await library.RenameSlotAsync(betaId, "alpha");
                Assert.IsFalse(r.IsSuccess, "Rename to an existing manual name should fail.");
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task RenameSlotAsync_UpdatesEntry()
        {
            var (_, library) = await AttachFreshAsync();
            try
            {
                var create = await library.CreateSlotAsync("Old");
                Assert.IsTrue(create.TryGetSuccess(out var slotId));

                Assert.IsTrue((await library.RenameSlotAsync(slotId, "New")).IsSuccess);

                var listed = await library.ListSlotsAsync();
                Assert.IsTrue(listed.TryGetSuccess(out var slots));
                Assert.AreEqual("New", slots.Single(s => s.Id == slotId).Name);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task ListSlotsAsync_OrdersAutoFirstThenManualByMostRecent()
        {
            var (_, library) = await AttachFreshAsync(seedLegacy: true);
            try
            {
                var a = await library.CreateSlotAsync("Alpha");
                await Task.Delay(5);
                var b = await library.CreateSlotAsync("Beta");
                Assert.IsTrue(a.TryGetSuccess(out var aId));
                Assert.IsTrue(b.TryGetSuccess(out var bId));

                var listed = await library.ListSlotsAsync();
                Assert.IsTrue(listed.TryGetSuccess(out var slots));
                Assert.HasCount(3, slots);
                Assert.AreEqual(SlotKind.Auto, slots[0].Kind);
                Assert.AreEqual(bId, slots[1].Id);
                Assert.AreEqual(aId, slots[2].Id);
            }
            finally { await library.DisposeAsync(); }
        }

        [TestMethod]
        public async Task LoadSlotAsync_HydratesStateAndSkipsMissingBlobs()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            var logger = new CapturingLogger<DndMapperLibraryService>();
            await using var library = new DndMapperLibraryService(db, engine, logger);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            // Build a snapshot with one map and two images; only image #1 has a backing blob.
            var mapId = Guid.NewGuid();
            var imgPresent = Guid.NewGuid();
            var imgMissing = Guid.NewGuid();

            var snapshot = new LibrarySnapshot
            {
                Maps =
                {
                    new MapSnapshot
                    {
                        Id = mapId,
                        Name = "Test Map",
                        ListOrder = 0,
                        CreatedUtc = DateTime.UtcNow,
                        Grid = new GridConfig(),
                        Images =
                        {
                            new MapImageSnapshot { Id = imgPresent, ContentType = "image/png", Width = 10, Height = 10, OriginalWidth = 100, OriginalHeight = 100 },
                            new MapImageSnapshot { Id = imgMissing, ContentType = "image/png", Width = 10, Height = 10, OriginalWidth = 100, OriginalHeight = 100 },
                        },
                    },
                },
            };

            var slotId = "slot-under-test";
            db.JsonStores[DndMapperLibrarySchema.LibraryStore][slotId] = snapshot;
            db.BlobStores[DndMapperLibrarySchema.ImagesStore][imgPresent.ToString("D")] =
                new FakeBlob(new byte[] { 1, 2, 3 }, "image/png");

            var load = await library.LoadSlotAsync(slotId);
            Assert.IsTrue(load.IsSuccess, "LoadSlotAsync should succeed even with a missing blob.");

            state.WithExclusiveRead(() =>
            {
                Assert.HasCount(1, state.Maps);
                var map = state.Maps[0];
                Assert.HasCount(1, map.Images, "Only the image with a backing blob should be hydrated.");
                Assert.AreEqual(imgPresent, map.Images[0].Id);
            });

            Assert.Contains(
                e => e.Message.Contains(imgMissing.ToString()), logger.Entries,
                "Missing-blob skip should log a warning naming the image id.");
        }

        // Builds an attached library against a fresh fake DB. When seedLegacy is
        // true, plants a v2 snapshot so the post-migration Auto Save slot exists
        // (required for ListSlotsAsync ordering tests).
        private static async Task<(FakeIndexedDbService Db, DndMapperLibraryService Library)> AttachFreshAsync(bool seedLegacy = false)
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            if (seedLegacy)
            {
                db.JsonStores[DndMapperLibrarySchema.LibraryStore] = new()
                {
                    [DndMapperLibrarySchema.LegacySingletonKey] = new LibrarySnapshot(),
                };
            }
            var library = new DndMapperLibraryService(db, engine, NullLogger<DndMapperLibraryService>.Instance);
            var attach = await library.AttachAsync(state, host);
            Assert.IsTrue(attach.IsSuccess, "AttachAsync failed in test setup.");
            return (db, library);
        }
    }
}
