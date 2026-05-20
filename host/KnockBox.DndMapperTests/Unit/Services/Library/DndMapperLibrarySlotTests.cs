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

                Assert.IsTrue(db.JsonStores[DndMapperLibrarySchema.LibraryStore].ContainsKey($"{slotId}:core"),
                    "v4 core shard must be written under {slotId}:core.");
                Assert.IsFalse(db.JsonStores[DndMapperLibrarySchema.LibraryStore].ContainsKey(slotId),
                    "Legacy v3 single-record key must not be written by manual save paths.");
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

        // Regression: a save where the host had a free-form Custom schema
        // active (so no NamedTemplate is tied to it) must still round-trip
        // the chosen initiative attribute. Previously the Combat panel hid
        // its dropdown after such a load because the attribute lived on the
        // (absent) NamedTemplate. The attribute now lives on state.
        [TestMethod]
        public async Task LoadSlotAsync_RoundTripsInitiativeAttribute_OnFreeFormCustomSchema()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            var customSchema = new KnockBox.DndMapper.Services.State.Games.Data.AttributeSchema(
                KnockBox.DndMapper.Models.AttributePreset.Custom,
                [
                    new KnockBox.DndMapper.Services.State.Games.Data.AttributeRow(
                        "Power",
                        KnockBox.DndMapper.Models.AttributeValueType.Score,
                        KnockBox.DndMapper.Services.State.Games.Data.AttributeValue.Score(12)),
                    new KnockBox.DndMapper.Services.State.Games.Data.AttributeRow(
                        "Grace",
                        KnockBox.DndMapper.Models.AttributeValueType.Score,
                        KnockBox.DndMapper.Services.State.Games.Data.AttributeValue.Score(14)),
                ]);
            Assert.IsTrue(engine.ChangeSchemaAsync(state, host, customSchema).IsSuccess);
            Assert.IsNull(state.ActiveSchemaTemplateId);
            Assert.IsTrue(engine.SetInitiativeAttributeAsync(state, host, "Grace").IsSuccess);
            Assert.AreEqual("Grace", state.InitiativeAttributeName);

            var create = await library.CreateSlotAsync("Round-Trip-FreeForm");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            var (engine2, state2, host2, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library2.AttachAsync(state2, host2)).IsSuccess);
            Assert.IsTrue((await library2.LoadSlotAsync(slotId)).IsSuccess);

            Assert.AreEqual("Grace", state2.InitiativeAttributeName,
                "After loading, the state-level initiative attribute should be restored verbatim.");
        }

        [TestMethod]
        public async Task LoadSlotAsync_RoundTripsFogMask()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "Catacombs").TryGetSuccess(out var mapId));
            Assert.IsTrue(engine.PaintFogAsync(state, host, mapId,
                new[] { (1, 1), (2, 1), (3, 1), (1, 2) }, fogged: true).IsSuccess);

            var create = await library.CreateSlotAsync("Fog-RoundTrip");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            var (engine2, state2, host2, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library2.AttachAsync(state2, host2)).IsSuccess);
            Assert.IsTrue((await library2.LoadSlotAsync(slotId)).IsSuccess);

            state2.WithExclusiveRead(() =>
            {
                var restoredMap = state2.Maps.Single(m => m.Id == mapId);
                Assert.IsTrue(restoredMap.IsFogged(1, 1));
                Assert.IsTrue(restoredMap.IsFogged(2, 1));
                Assert.IsTrue(restoredMap.IsFogged(3, 1));
                Assert.IsTrue(restoredMap.IsFogged(1, 2));
                Assert.IsFalse(restoredMap.IsFogged(0, 0));
                Assert.IsFalse(restoredMap.IsFogged(4, 1));
            });
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
