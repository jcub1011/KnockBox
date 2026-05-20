using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    /// <summary>
    /// Covers the v4 per-slot sharded layout: a token move on Map A should
    /// only rewrite Map A's shard plus the core spine, not every map's fog
    /// mask. Also covers the v3→v4 single-record auto-migration on first
    /// load, the missing-shard skip, and full-slot deletion.
    /// </summary>
    [TestClass]
    public class SaveShardingTests
    {
        [TestMethod]
        public async Task Flush_AfterAttach_WritesCoreAndAllShards()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "Forest").TryGetSuccess(out var mapA));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Dungeon").TryGetSuccess(out var mapB));
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Hero").TryGetSuccess(out var sheetId));

            await library.ForTestingFlushAsync();

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            var auto = DndMapperLibrarySchema.AutoSlotId;
            Assert.IsTrue(libStore.ContainsKey($"{auto}:core"), "Core spine must be written.");
            Assert.IsTrue(libStore.ContainsKey($"{auto}:map:{mapA:D}"), "Map A shard must be written.");
            Assert.IsTrue(libStore.ContainsKey($"{auto}:map:{mapB:D}"), "Map B shard must be written.");
            Assert.IsTrue(libStore.ContainsKey($"{auto}:sheet:{sheetId:D}"), "Sheet shard must be written.");
            Assert.IsFalse(libStore.ContainsKey(auto), "Legacy v3 single-record key must NOT be written.");
        }

        [TestMethod]
        public async Task Flush_AfterTokenMoveOnOneMap_WritesOnlyThatMapShardAndCore()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "A").TryGetSuccess(out var mapA));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "B").TryGetSuccess(out var mapB));
            Assert.IsTrue(engine.SetActiveMapAsync(state, host, mapA).IsSuccess);
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, mapA, "Goblin")
                .TryGetSuccess(out var goblin));
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Hero")
                .TryGetSuccess(out var sheetId));

            // First flush — populates the per-shard hash cache.
            await library.ForTestingFlushAsync();

            // Snapshot the in-store payloads so we can detect rewrites by
            // reference identity (FakeIndexedDb stores the raw object).
            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            var auto = DndMapperLibrarySchema.AutoSlotId;
            var mapAPayloadBefore = libStore[$"{auto}:map:{mapA:D}"];
            var mapBPayloadBefore = libStore[$"{auto}:map:{mapB:D}"];
            var sheetPayloadBefore = libStore[$"{auto}:sheet:{sheetId:D}"];
            var corePayloadBefore = libStore[$"{auto}:core"];

            // Mutate a token on Map A only.
            Assert.IsTrue(engine.MoveTokenAsync(state, host, goblin, 3, 4).IsSuccess);

            await library.ForTestingFlushAsync();

            // Map A shard must be rewritten (its tokens changed).
            Assert.AreNotSame(mapAPayloadBefore, libStore[$"{auto}:map:{mapA:D}"],
                "Map A shard must be rewritten — its tokens changed.");
            // Map B shard untouched — token move didn't affect it.
            Assert.AreSame(mapBPayloadBefore, libStore[$"{auto}:map:{mapB:D}"],
                "Map B shard must NOT be rewritten — nothing on Map B changed.");
            // Sheet shard untouched.
            Assert.AreSame(sheetPayloadBefore, libStore[$"{auto}:sheet:{sheetId:D}"],
                "Sheet shard must NOT be rewritten — no sheet changed.");
            // Core is untouched because map/sheet ids and global fields are unchanged.
            Assert.AreSame(corePayloadBefore, libStore[$"{auto}:core"],
                "Core spine must NOT be rewritten — no maps added/removed and no global fields changed.");
        }

        [TestMethod]
        public async Task Flush_AfterMapRemoval_DeletesShardAndUpdatesCore()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "Keep").TryGetSuccess(out var keepId));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Drop").TryGetSuccess(out var dropId));

            await library.ForTestingFlushAsync();

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            var auto = DndMapperLibrarySchema.AutoSlotId;
            Assert.IsTrue(libStore.ContainsKey($"{auto}:map:{dropId:D}"), "Drop shard must exist before delete.");

            Assert.IsTrue(engine.DeleteMapAsync(state, host, dropId).IsSuccess);
            await library.ForTestingFlushAsync();

            Assert.IsFalse(libStore.ContainsKey($"{auto}:map:{dropId:D}"),
                "Deleted map's shard must be removed from IDB on the next flush.");
            Assert.IsTrue(libStore.ContainsKey($"{auto}:map:{keepId:D}"),
                "Surviving map's shard must remain.");
        }

        [TestMethod]
        public async Task LoadSlotAsync_AutoMigratesV3SingleRecord_AndDeletesLegacyKey()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();

            // Plant a v3 single-record under a manual slot id BEFORE attach.
            // The slots index will reflect the slot's existence after migration.
            var slotId = Guid.NewGuid().ToString("D");
            var mapId = Guid.NewGuid();
            var sheetId = Guid.NewGuid();
            var legacy = new LibrarySnapshot
            {
                SchemaVersion = 3,
                Maps =
                {
                    new MapSnapshot
                    {
                        Id = mapId,
                        Name = "Migrated",
                        ListOrder = 0,
                        CreatedUtc = DateTime.UtcNow,
                        Grid = new GridConfig(),
                    },
                },
                Sheets =
                {
                    new SheetSnapshot { Id = sheetId, CharacterName = "Migrated Hero" },
                },
            };
            db.JsonStores[DndMapperLibrarySchema.LibraryStore] = new()
            {
                [slotId] = legacy,
            };

            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            // Trigger migration via LoadSlotAsync.
            Assert.IsTrue((await library.LoadSlotAsync(slotId)).IsSuccess);

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            Assert.IsFalse(libStore.ContainsKey(slotId),
                "Legacy v3 single-record key must be removed after migration.");
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:core"),
                "v4 core shard must be written by the migration.");
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:map:{mapId:D}"),
                "v4 map shard must be written by the migration.");
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:sheet:{sheetId:D}"),
                "v4 sheet shard must be written by the migration.");

            // Hydration also worked.
            state.WithExclusiveRead(() =>
            {
                Assert.HasCount(1, state.Maps);
                Assert.AreEqual("Migrated", state.Maps[0].Name);
                Assert.AreEqual(1, state.Sheets.Count);
                Assert.AreEqual("Migrated Hero", state.Sheets[sheetId].CharacterName);
            });
        }

        [TestMethod]
        public async Task LoadSlotAsync_WithMissingMapShard_SkipsItAndHydratesRest()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            var logger = new CapturingLogger<DndMapperLibraryService>();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, logger);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            // Build a populated slot, then manually delete one map shard to
            // simulate a corrupt/partial IDB state.
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Keep").TryGetSuccess(out var keepId));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Gone").TryGetSuccess(out var goneId));
            var create = await library.CreateSlotAsync("CorruptTest");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            libStore.Remove($"{slotId}:map:{goneId:D}");

            // Detach + re-attach with a fresh state so LoadSlotAsync runs cold.
            var (engine2, state2, host2, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullJsRuntime.Instance, logger);
            Assert.IsTrue((await library2.AttachAsync(state2, host2)).IsSuccess);

            Assert.IsTrue((await library2.LoadSlotAsync(slotId)).IsSuccess);

            state2.WithExclusiveRead(() =>
            {
                Assert.HasCount(1, state2.Maps);
                Assert.AreEqual(keepId, state2.Maps[0].Id);
            });
            Assert.Contains(
                e => e.Message.Contains(goneId.ToString()), logger.Entries,
                "Missing-shard skip should log a warning naming the map id.");
        }

        [TestMethod]
        public async Task DeleteSlotAsync_RemovesEveryShard()
        {
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "M1").TryGetSuccess(out var m1));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "M2").TryGetSuccess(out var m2));
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "S1")
                .TryGetSuccess(out var s1));

            var create = await library.CreateSlotAsync("ToDelete");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:core"));
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:map:{m1:D}"));
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:map:{m2:D}"));
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:sheet:{s1:D}"));

            Assert.IsTrue((await library.DeleteSlotAsync(slotId)).IsSuccess);

            Assert.IsFalse(libStore.ContainsKey($"{slotId}:core"), "Core shard must be deleted.");
            Assert.IsFalse(libStore.ContainsKey($"{slotId}:map:{m1:D}"), "Map shard m1 must be deleted.");
            Assert.IsFalse(libStore.ContainsKey($"{slotId}:map:{m2:D}"), "Map shard m2 must be deleted.");
            Assert.IsFalse(libStore.ContainsKey($"{slotId}:sheet:{s1:D}"), "Sheet shard must be deleted.");

            var listed = await library.ListSlotsAsync();
            Assert.IsTrue(listed.TryGetSuccess(out var slots));
            Assert.DoesNotContain(s => s.Id == slotId, slots);
        }

        [TestMethod]
        public async Task SaveToSlotAsync_OverwriteAfterMapRemoval_DeletesStaleShards()
        {
            // Create a manual slot with two maps, then drop one map in state
            // and overwrite the slot. The dropped map's shard must be removed
            // from IDB so a future load doesn't resurrect it.
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "Keep").TryGetSuccess(out var keep));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Drop").TryGetSuccess(out var drop));
            var create = await library.CreateSlotAsync("WithDrop");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            var libStore = db.JsonStores[DndMapperLibrarySchema.LibraryStore];
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:map:{drop:D}"));

            Assert.IsTrue(engine.DeleteMapAsync(state, host, drop).IsSuccess);
            Assert.IsTrue((await library.SaveToSlotAsync(slotId)).IsSuccess);

            Assert.IsFalse(libStore.ContainsKey($"{slotId}:map:{drop:D}"),
                "Stale map shard must be removed after a manual overwrite.");
            Assert.IsTrue(libStore.ContainsKey($"{slotId}:map:{keep:D}"),
                "Surviving map shard must remain.");
        }
    }
}
