using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.DndMapperTests.Unit.Services.Library
{
    /// <summary>
    /// Regression coverage for the save/load responsiveness refactor:
    /// (1) LoadSlotAsync now pre-builds maps/sheets/templates OFF the state
    ///     Execute lock; only the bulk swap happens inside.
    /// (2) HydrateImagesFromSnapshotAsync fetches blobs in parallel
    ///     (Task.WhenAll) and applies cache mutations serially.
    /// (3) CreateSlotAsync / SaveToSlotAsync offload TakeSnapshot to a
    ///     thread-pool thread via Task.Run.
    /// Each test exercises a round-trip and asserts the end state is what
    /// the pre-refactor code produced.
    /// </summary>
    [TestClass]
    public class SaveLoadResponsivenessTests
    {
        // ── (1) Pre-build outside Execute ─────────────────────────────────────

        [TestMethod]
        public async Task LoadSlotAsync_RoundTripsAllCollections_AfterPreBuildRefactor()
        {
            // Stage a slot that exercises every collection BuildHydration
            // touches: multiple maps with images + tokens + fog, multiple
            // sheets with attributes + status effects + roll templates, plus
            // a global roll template. After round-trip, all of these must
            // land in state with the same contents.
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            // Two maps, fog on the first.
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Outer").TryGetSuccess(out var outerId));
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Inner").TryGetSuccess(out var innerId));
            Assert.IsTrue(engine.PaintFogAsync(state, host, outerId,
                new[] { (0, 0), (1, 0), (2, 0) }, fogged: true).IsSuccess);

            // NPCs on each map.
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, outerId, "Goblin").IsSuccess);
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, innerId, "Orc").IsSuccess);

            // A sheet with a sheet-scoped roll template.
            Assert.IsTrue(engine.CreateSheetAsync(state, host, ownerUserId: null, "Hero")
                .TryGetSuccess(out var sheetId));
            Assert.IsTrue(engine.CreateSheetRollTemplateAsync(
                state, host, sheetId, name: "PowerStrike",
                dice: [new DiceTerm(2, 8)], flatModifier: 3, mode: RollMode.Normal,
                attributeName: null, label: "+3 force").IsSuccess);

            // A global roll template.
            Assert.IsTrue(engine.CreateGlobalRollTemplateAsync(
                state, host, name: "Initiative",
                dice: [new DiceTerm(1, 20)], flatModifier: 0, mode: RollMode.Normal,
                attributeName: "DEX", label: "init").IsSuccess);

            // Round-trip via a named slot.
            var create = await library.CreateSlotAsync("PreBuildRT");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            // Fresh state on the other end so the load actually has to rebuild.
            var (engine2, state2, host2, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library2.AttachAsync(state2, host2)).IsSuccess);
            Assert.IsTrue((await library2.LoadSlotAsync(slotId)).IsSuccess);

            state2.WithExclusiveRead(() =>
            {
                Assert.HasCount(2, state2.Maps);
                Assert.AreEqual(outerId, state2.Maps[0].Id, "ListOrder must be preserved by BuildHydration.");
                Assert.AreEqual(innerId, state2.Maps[1].Id);
                Assert.IsTrue(state2.Maps[0].IsFogged(0, 0), "Fog mask must round-trip through the pre-build path.");
                Assert.HasCount(1, state2.Maps[0].Tokens);
                Assert.AreEqual("Goblin", state2.Maps[0].Tokens[0].Name);
                Assert.AreEqual(TokenType.NPCToken, state2.Maps[0].Tokens[0].Type);
                Assert.HasCount(1, state2.Maps[1].Tokens);

                Assert.AreEqual(1, state2.Sheets.Count);
                Assert.IsTrue(state2.Sheets.TryGetValue(sheetId, out var sheet));
                Assert.AreEqual("Hero", sheet!.CharacterName);
                Assert.HasCount(1, sheet.RollTemplates);
                Assert.AreEqual("PowerStrike", sheet.RollTemplates[0].Name);

                Assert.HasCount(1, state2.GlobalRollTemplates);
                Assert.AreEqual("Initiative", state2.GlobalRollTemplates[0].Name);

                // First map must be the active one after load.
                Assert.AreEqual(outerId, state2.ActiveMapId);
            });
        }

        [TestMethod]
        public async Task LoadSlotAsync_RoundTripsCustomNamedTemplate_AfterPreBuildRefactor()
        {
            // Custom (user-authored) NamedTemplate exercises the non-built-in
            // branch of the template overlay loop inside Execute.
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            var customSchema = new AttributeSchema(
                AttributePreset.Custom,
                [
                    new AttributeRow("Power", AttributeValueType.Score, AttributeValue.Score(12)),
                    new AttributeRow("Grace", AttributeValueType.Score, AttributeValue.Score(14)),
                ]);
            Assert.IsTrue(engine.ChangeSchemaAsync(state, host, customSchema).IsSuccess);

            Assert.IsTrue(engine.CreateCustomTemplateAsync(state, host, "CustomKit",
                [
                    new AttributeRow("Power", AttributeValueType.Score, AttributeValue.Score(10)),
                    new AttributeRow("Grace", AttributeValueType.Score, AttributeValue.Score(10)),
                ]).TryGetSuccess(out var templateId));

            var create = await library.CreateSlotAsync("CustomTemplateRT");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            var (engine2, state2, host2, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library2.AttachAsync(state2, host2)).IsSuccess);
            Assert.IsTrue((await library2.LoadSlotAsync(slotId)).IsSuccess);

            state2.WithExclusiveRead(() =>
            {
                Assert.IsTrue(state2.CustomTemplates.TryGetValue(templateId, out var t));
                Assert.AreEqual("CustomKit", t!.Name);
                Assert.IsFalse(t.IsBuiltIn, "User-authored template must round-trip through the non-built-in branch of the overlay loop.");
                Assert.HasCount(2, t.Rows);
            });
        }

        // ── (2) Parallel blob fetch ───────────────────────────────────────────

        [TestMethod]
        public async Task HydrateImagesFromSnapshot_FetchesMultipleBlobsConcurrently_PreservingOrderAndSkippingMissing()
        {
            // Plant a snapshot with five images across two maps. Two of the
            // five have no backing blob in IDB. After load, only the three
            // present blobs hydrate, in their original LayerOrder per map.
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            var logger = new CapturingLogger<DndMapperLibraryService>();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, logger);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            var mapA = Guid.NewGuid();
            var mapB = Guid.NewGuid();
            var img1 = Guid.NewGuid(); // present
            var img2 = Guid.NewGuid(); // missing
            var img3 = Guid.NewGuid(); // present
            var img4 = Guid.NewGuid(); // missing
            var img5 = Guid.NewGuid(); // present

            var snapshot = new LibrarySnapshot
            {
                Maps =
                {
                    new MapSnapshot
                    {
                        Id = mapA, Name = "A", ListOrder = 0, CreatedUtc = DateTime.UtcNow,
                        Grid = new GridConfig(),
                        Images =
                        {
                            new MapImageSnapshot { Id = img1, ContentType = "image/png", LayerOrder = 0, Width = 1, Height = 1 },
                            new MapImageSnapshot { Id = img2, ContentType = "image/png", LayerOrder = 1, Width = 1, Height = 1 },
                            new MapImageSnapshot { Id = img3, ContentType = "image/png", LayerOrder = 2, Width = 1, Height = 1 },
                        },
                    },
                    new MapSnapshot
                    {
                        Id = mapB, Name = "B", ListOrder = 1, CreatedUtc = DateTime.UtcNow,
                        Grid = new GridConfig(),
                        Images =
                        {
                            new MapImageSnapshot { Id = img4, ContentType = "image/png", LayerOrder = 0, Width = 1, Height = 1 },
                            new MapImageSnapshot { Id = img5, ContentType = "image/png", LayerOrder = 1, Width = 1, Height = 1 },
                        },
                    },
                },
            };

            var slotId = Guid.NewGuid().ToString("D");
            db.JsonStores[DndMapperLibrarySchema.LibraryStore][slotId] = snapshot;
            db.BlobStores[DndMapperLibrarySchema.ImagesStore][img1.ToString("D")] = new FakeBlob(new byte[] { 1 }, "image/png");
            db.BlobStores[DndMapperLibrarySchema.ImagesStore][img3.ToString("D")] = new FakeBlob(new byte[] { 3 }, "image/png");
            db.BlobStores[DndMapperLibrarySchema.ImagesStore][img5.ToString("D")] = new FakeBlob(new byte[] { 5 }, "image/png");

            Assert.IsTrue((await library.LoadSlotAsync(slotId)).IsSuccess);

            state.WithExclusiveRead(() =>
            {
                Assert.HasCount(2, state.Maps);
                Assert.HasCount(2, state.Maps[0].Images);
                Assert.AreEqual(img1, state.Maps[0].Images[0].Id, "LayerOrder must be preserved after parallel fetch.");
                Assert.AreEqual(img3, state.Maps[0].Images[1].Id);
                Assert.HasCount(1, state.Maps[1].Images);
                Assert.AreEqual(img5, state.Maps[1].Images[0].Id);
            });

            // Both missing-blob ids must surface a warning, even though the
            // fetches run in parallel.
            Assert.Contains(e => e.Message.Contains(img2.ToString()), logger.Entries);
            Assert.Contains(e => e.Message.Contains(img4.ToString()), logger.Entries);
        }

        // ── (3) Manual-save snapshot off the circuit ──────────────────────────

        [TestMethod]
        public async Task CreateAndSaveToSlotAsync_RoundTripsCorrectlyAfterTaskRunOffload()
        {
            // The Task.Run wrap is invisible from the outside — assert that
            // CreateSlot → SaveToSlot → LoadSlot produces the same state we
            // started with. This is the witness that Task.Run(TakeSnapshot)
            // didn't lose anything in the offload.
            var (engine, state, host, _) = EngineTestFactory.Build();
            var db = new FakeIndexedDbService();
            await using var library = new DndMapperLibraryService(db, engine, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library.AttachAsync(state, host)).IsSuccess);

            Assert.IsTrue(engine.CreateMapAsync(state, host, "Arena").TryGetSuccess(out var arenaId));
            Assert.IsTrue(engine.SpawnNpcTokenAsync(state, host, arenaId, "Wolf")
                .TryGetSuccess(out var wolfId));

            var create = await library.CreateSlotAsync("OffloadRT");
            Assert.IsTrue(create.TryGetSuccess(out var slotId));

            // Mutate state, then overwrite the slot — exercises SaveToSlotAsync's
            // Task.Run path including the stale-shard cleanup.
            Assert.IsTrue(engine.MoveTokenAsync(state, host, wolfId, 5, 7).IsSuccess);
            Assert.IsTrue(engine.CreateMapAsync(state, host, "Cellar").TryGetSuccess(out var cellarId));
            Assert.IsTrue((await library.SaveToSlotAsync(slotId)).IsSuccess);

            var (engine2, state2, host2, _) = EngineTestFactory.Build();
            await using var library2 = new DndMapperLibraryService(db, engine2, NullJsRuntime.Instance, NullLogger<DndMapperLibraryService>.Instance);
            Assert.IsTrue((await library2.AttachAsync(state2, host2)).IsSuccess);
            Assert.IsTrue((await library2.LoadSlotAsync(slotId)).IsSuccess);

            state2.WithExclusiveRead(() =>
            {
                Assert.HasCount(2, state2.Maps);
                var arena = state2.Maps.Single(m => m.Id == arenaId);
                Assert.HasCount(1, arena.Tokens);
                Assert.AreEqual(5.0, arena.Tokens[0].X);
                Assert.AreEqual(7.0, arena.Tokens[0].Y);
                Assert.IsTrue(state2.Maps.Any(m => m.Id == cellarId));
            });
        }
    }
}
