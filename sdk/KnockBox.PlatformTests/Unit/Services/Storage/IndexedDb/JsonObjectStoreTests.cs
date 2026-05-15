using System.Text.Json;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class JsonObjectStoreTests
{
    [TestMethod]
    public async Task GetAsync_NullValue_ReturnsNull()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGet", "null");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetSuccess(out var value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public async Task GetAsync_HappyPath_ReturnsClonedElement()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGet", "{\"foo\":42}");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetSuccess(out var value));
        Assert.IsNotNull(value);
        Assert.AreEqual(42, value!.Value.GetProperty("foo").GetInt32());
    }

    [TestMethod]
    public async Task GetAsync_InteropError_Propagated()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawFailure("storeGet", new IndexedDbError(IndexedDbErrorKind.Data, "bad data"));
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task GetAsync_Canceled_Propagates()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawCanceled("storeGet");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.IsCanceled);
    }

    [TestMethod]
    public async Task GetAsync_InactiveTransaction_ShortCircuits()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var store = new JsonObjectStore(ctx, "things");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, err.Kind);
        interop.Verify(x => x.InvokeRawAsync("storeGet", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task GetAllAsync_HappyPath_ClonesElements()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGetAll", "[{\"a\":1},{\"a\":2}]");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var result = await store.GetAllAsync();
        Assert.IsTrue(result.TryGetSuccess(out var list));
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(2, list[1].GetProperty("a").GetInt32());
    }

    [TestMethod]
    public async Task AddAsync_HappyPath_ReturnsKey()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeAdd", IndexedDbTestHelpers.NumberKeyJson(5));
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var element = JsonSerializer.Deserialize<JsonElement>("{\"x\":1}");
        var result = await store.AddAsync(element);
        Assert.IsTrue(result.TryGetSuccess(out var key));
        Assert.AreEqual(5.0, (double)key.Value!);
    }

    [TestMethod]
    public async Task PutAsync_HappyPath_ReturnsKey()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storePut", IndexedDbTestHelpers.NumberKeyJson(6));
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        var element = JsonSerializer.Deserialize<JsonElement>("{\"x\":2}");
        var result = await store.PutAsync(element);
        Assert.IsTrue(result.TryGetSuccess(out var key));
        Assert.AreEqual(6.0, (double)key.Value!);
    }

    [TestMethod]
    public async Task Inactive_AddOrPut_ShortCircuit()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var store = new JsonObjectStore(ctx, "things");

        var add = await store.AddAsync(JsonSerializer.Deserialize<JsonElement>("{}"));
        var put = await store.PutAsync(JsonSerializer.Deserialize<JsonElement>("{}"));

        Assert.IsTrue(add.TryGetFailure(out var addErr) && addErr.Kind == IndexedDbErrorKind.TransactionInactive);
        Assert.IsTrue(put.TryGetFailure(out var putErr) && putErr.Kind == IndexedDbErrorKind.TransactionInactive);
        interop.Verify(x => x.InvokeRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public void Index_UnknownIndex_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new JsonObjectStore(ctx, "things");

        Assert.ThrowsExactly<InvalidOperationException>(() => store.Index("any"));
    }

    [TestMethod]
    public void Index_Known_ReturnsJsonIndex()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object)
            .WithIndex("things", "byA", new IndexSchema(new[] { "a" }, false, false));
        var store = new JsonObjectStore(ctx, "things");

        var idx = store.Index("byA");
        Assert.IsInstanceOfType<JsonIndex>(idx);
        Assert.AreEqual("byA", idx.Name);
    }
}
