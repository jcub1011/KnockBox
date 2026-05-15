using System.Text.Json;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class ObjectStoreTests
{
    public sealed record User(string Name, int Age);

    [TestMethod]
    public async Task GetAsync_HappyPath_DeserializesValue()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGet", "{\"name\":\"alice\",\"age\":30}");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAsync(IndexedDbKey.String("a"));

        Assert.IsTrue(result.TryGetSuccess(out var user));
        Assert.AreEqual("alice", user!.Name);
    }

    [TestMethod]
    public async Task GetAsync_NullJsonValue_ReturnsDefault()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGet", "null");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAsync(1);

        Assert.IsTrue(result.TryGetSuccess(out var user));
        Assert.IsNull(user);
    }

    [TestMethod]
    public async Task GetAsync_DeserializationFailure_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        // age is required to be int but the value is a string — deserialization throws.
        interop.SetupRawSuccess("storeGet", "{\"name\":\"alice\",\"age\":\"not-a-number\"}");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAsync(1);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task GetAsync_InteropError_Propagated()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawFailure("storeGet", new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "gone"));
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, err.Kind);
    }

    [TestMethod]
    public async Task GetAsync_Canceled_ReturnsCanceled()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawCanceled("storeGet");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.IsCanceled);
    }

    [TestMethod]
    public async Task GetAsync_InactiveTransaction_ShortCircuits()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, err.Kind);
        interop.Verify(x => x.InvokeRawAsync("storeGet", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task GetAllAsync_HappyPath_DeserializesArray()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGetAll",
            "[{\"name\":\"a\",\"age\":1},{\"name\":\"b\",\"age\":2}]");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAllAsync();

        Assert.IsTrue(result.TryGetSuccess(out var users));
        Assert.AreEqual(2, users.Count);
        Assert.AreEqual("a", users[0].Name);
    }

    [TestMethod]
    public async Task GetAllAsync_DeserializationFailure_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGetAll", "[{\"name\":\"alice\",\"age\":\"not-a-number\"}]");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAllAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task AddAsync_ReturnsEffectiveKey()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeAdd", IndexedDbTestHelpers.NumberKeyJson(42));
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.AddAsync(new User("a", 1));
        Assert.IsTrue(result.TryGetSuccess(out var key));
        Assert.AreEqual(IndexedDbKeyKind.Number, key.Kind);
        Assert.AreEqual(42.0, (double)key.Value!);
    }

    [TestMethod]
    public async Task PutAsync_ReturnsEffectiveKey()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storePut", IndexedDbTestHelpers.StringKeyJson("abc"));
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.PutAsync(new User("a", 1));
        Assert.IsTrue(result.TryGetSuccess(out var key));
        Assert.AreEqual("abc", (string)key.Value!);
    }

    [TestMethod]
    public async Task AddOrPutAsync_MalformedKeyEnvelope_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storePut", "{\"oops\":true}");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.PutAsync(new User("a", 1));
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task DeleteAsync_RoutesToStoreDelete()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("storeDelete");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.DeleteAsync(IndexedDbKey.Number(1));
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task DeleteRangeAsync_RoutesToStoreDeleteRange()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("storeDeleteRange");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.DeleteRangeAsync(KeyRange.Bound(1, 10));
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task ClearAsync_RoutesToStoreClear()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("storeClear");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.ClearAsync();
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task CountAsync_ReturnsLong()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeCount", "42");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.CountAsync();
        Assert.IsTrue(result.TryGetSuccess(out var n));
        Assert.AreEqual(42L, n);
    }

    [TestMethod]
    public async Task GetAllKeysAsync_DeserializesKeys()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGetAllKeys",
            $"[{IndexedDbTestHelpers.NumberKeyJson(1)},{IndexedDbTestHelpers.NumberKeyJson(2)}]");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAllKeysAsync();
        Assert.IsTrue(result.TryGetSuccess(out var keys));
        Assert.AreEqual(2, keys.Count);
        Assert.AreEqual(1.0, (double)keys[0].Value!);
    }

    [TestMethod]
    public async Task GetAllKeysAsync_MalformedKey_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("storeGetAllKeys", "[{\"bogus\":true}]");
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        var result = await store.GetAllKeysAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public void Index_UnknownIndex_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = new ObjectStore<User>(ctx, "users");

        Assert.ThrowsExactly<InvalidOperationException>(() => store.Index("byName"));
    }

    [TestMethod]
    public void Index_KnownIndex_ReturnsTypedIndex()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object)
            .WithIndex("users", "byName", new IndexSchema(new[] { "name" }, Unique: false, MultiEntry: false));
        var store = new ObjectStore<User>(ctx, "users");

        var idx = store.Index("byName");
        Assert.AreEqual("byName", idx.Name);
        Assert.IsFalse(idx.Unique);
    }
}
