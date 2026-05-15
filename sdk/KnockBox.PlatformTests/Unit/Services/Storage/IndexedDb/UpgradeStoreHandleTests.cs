using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class UpgradeStoreHandleTests
{
    private static UpgradeContext NewCtx(IndexedDbInterop interop)
    {
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        return new UpgradeContext(
            interop, NullLoggerFactory.Instance, registry,
            upgradeTxId: 1, oldVersion: 0, newVersion: 1,
            jsonOptions: new JsonSerializerOptions(),
            existingSchema: new Dictionary<string, IReadOnlyList<string>>());
    }

    [TestMethod]
    public void CreateIndex_DuplicateName_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = NewCtx(interop.Object);
        var store = ctx.CreateJsonObjectStore("things");
        store.CreateIndex("byName", "name");

        Assert.ThrowsExactly<InvalidOperationException>(() => store.CreateIndex("byName", "name"));
    }

    [TestMethod]
    public void DeleteIndex_Unknown_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = NewCtx(interop.Object);
        var store = ctx.CreateJsonObjectStore("things");

        Assert.ThrowsExactly<InvalidOperationException>(() => store.DeleteIndex("nope"));
    }

    [TestMethod]
    public void CreateIndex_TracksInIndexNames()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = NewCtx(interop.Object);
        var store = ctx.CreateJsonObjectStore("things");

        store.CreateIndex("byName", "name");
        store.CreateIndex("byAge", "age");
        CollectionAssert.AreEqual(new[] { "byName", "byAge" }, store.IndexNames.ToArray());

        store.DeleteIndex("byAge");
        CollectionAssert.AreEqual(new[] { "byName" }, store.IndexNames.ToArray());
    }

    [TestMethod]
    public async Task CreateIndex_DeleteIndex_ReachJsInOrder_OnFlush()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        SchemaOp[]? capturedBatch = null;
        interop.Setup(x => x.InvokeVoidAsync(
            "upgradeApplySchemaOps", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedBatch = (SchemaOp[])args[1]!;
                return new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success);
            });

        var ctx = NewCtx(interop.Object);
        var store = ctx.CreateJsonObjectStore("things");
        store.CreateIndex("byName", "name");
        store.CreateIndex("byAge", "age", unique: true);
        store.DeleteIndex("byName");

        // Awaiting an async data accessor flushes the queue.
        await ctx.JsonObjectStoreAsync("things");

        Assert.IsNotNull(capturedBatch);
        Assert.AreEqual(4, capturedBatch!.Length);
        Assert.AreEqual("createStore", capturedBatch[0].Type);
        Assert.AreEqual("createIndex", capturedBatch[1].Type);
        Assert.AreEqual("byName", capturedBatch[1].Name);
        Assert.AreEqual("createIndex", capturedBatch[2].Type);
        Assert.AreEqual(true, capturedBatch[2].Unique);
        Assert.AreEqual("deleteIndex", capturedBatch[3].Type);
        Assert.AreEqual("byName", capturedBatch[3].Name);
    }

    [TestMethod]
    public void IndexSchema_RoundTripsCompositeKeyPath()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = NewCtx(interop.Object);
        var store = ctx.CreateJsonObjectStore("things");
        store.CreateIndex("compound", KeyPath.Composite("a", "b"));
        CollectionAssert.AreEqual(new[] { "compound" }, store.IndexNames.ToArray());
    }
}

[TestClass]
public sealed class UpgradeTxContextTests
{
    [TestMethod]
    public void IsActive_Tracks_IsActiveDelegate()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var active = true;
        var ctx = new UpgradeTxContext(interop.Object, 7, new JsonSerializerOptions(), () => active);

        Assert.IsTrue(ctx.IsActive);
        active = false;
        Assert.IsFalse(ctx.IsActive);
    }

    [TestMethod]
    public void TryGetIndexSchema_AlwaysReturnsFalse_DuringUpgrade()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new UpgradeTxContext(interop.Object, 7, new JsonSerializerOptions(), () => true);

        Assert.IsFalse(ctx.TryGetIndexSchema("store", "idx", out _));
    }

    [TestMethod]
    public void TxId_AndJsonOptions_AreSurfacedFromCtor()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var opts = new JsonSerializerOptions();
        var ctx = new UpgradeTxContext(interop.Object, 42, opts, () => true);

        Assert.AreEqual(42, ctx.TxId);
        Assert.AreSame(opts, ctx.JsonOptions);
        Assert.AreSame(interop.Object, ctx.Interop);
    }
}
