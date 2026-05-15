using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexTests
{
    public sealed record User(string Name);

    private static TypedIndex<User> NewTyped(IndexedDbInterop interop, bool active = true, bool composite = false, bool unique = false, bool multi = false)
    {
        var schema = new IndexSchema(
            KeyPath: composite ? new[] { "a", "b" } : new[] { "name" },
            Unique: unique,
            MultiEntry: multi);
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop) { IsActive = active };
        return new TypedIndex<User>(ctx, "users", "byName", schema);
    }

    private static JsonIndex NewJson(IndexedDbInterop interop, bool active = true)
    {
        var schema = new IndexSchema(new[] { "name" }, false, false);
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop) { IsActive = active };
        return new JsonIndex(ctx, "users", "byName", schema);
    }

    // ────────── TypedIndex<T> ──────────

    [TestMethod]
    public void TypedIndex_Composite_KeyPath_IsComposite()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var idx = NewTyped(interop.Object, composite: true);
        Assert.IsTrue(idx.KeyPath.IsComposite);
        Assert.AreEqual(2, idx.KeyPath.Paths.Count);
    }

    [TestMethod]
    public void TypedIndex_Single_KeyPath_IsSingle()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var idx = NewTyped(interop.Object);
        Assert.IsFalse(idx.KeyPath.IsComposite);
    }

    [TestMethod]
    public async Task TypedIndex_GetAsync_NullValue_ReturnsDefault()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexGet", "null");
        var idx = NewTyped(interop.Object);

        var result = await idx.GetAsync("alice");
        Assert.IsTrue(result.TryGetSuccess(out var user));
        Assert.IsNull(user);
    }

    [TestMethod]
    public async Task TypedIndex_GetAsync_HappyPath_Deserializes()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexGet", "{\"name\":\"alice\"}");
        var idx = NewTyped(interop.Object);

        var result = await idx.GetAsync("alice");
        Assert.IsTrue(result.TryGetSuccess(out var user));
        Assert.AreEqual("alice", user!.Name);
    }

    [TestMethod]
    public async Task TypedIndex_GetAsync_BadJson_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        // User.Name is a string — pass a number in a way that can't be coerced.
        interop.SetupRawSuccess("indexGet", "[1,2,3]");
        var idx = NewTyped(interop.Object);

        var result = await idx.GetAsync("alice");
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task TypedIndex_GetAllAsync_BadJson_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        // Outer is an array of arrays; inner array cannot deserialize to User.
        interop.SetupRawSuccess("indexGetAll", "[[1,2,3]]");
        var idx = NewTyped(interop.Object);

        var result = await idx.GetAllAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task TypedIndex_GetAllKeysAsync_BadEnvelope_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexGetAllKeys", "[{\"bogus\":1}]");
        var idx = NewTyped(interop.Object);

        var result = await idx.GetAllKeysAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task TypedIndex_CountAsync_HappyPath()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexCount", "5");
        var idx = NewTyped(interop.Object);

        var result = await idx.CountAsync();
        Assert.IsTrue(result.TryGetSuccess(out var n));
        Assert.AreEqual(5L, n);
    }

    [TestMethod]
    public async Task TypedIndex_InactiveTx_AllOpsShortCircuit()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var idx = NewTyped(interop.Object, active: false);

        Assert.IsTrue((await idx.GetAsync("a")).TryGetFailure(out _));
        Assert.IsTrue((await idx.GetAllAsync()).TryGetFailure(out _));
        Assert.IsTrue((await idx.GetAllKeysAsync()).TryGetFailure(out _));
        Assert.IsTrue((await idx.CountAsync()).TryGetFailure(out _));
        interop.Verify(x => x.InvokeRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    // ────────── JsonIndex ──────────

    [TestMethod]
    public async Task JsonIndex_GetAsync_NullValue_ReturnsNull()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexGet", "null");
        var idx = NewJson(interop.Object);

        var result = await idx.GetAsync("a");
        Assert.IsTrue(result.TryGetSuccess(out var v));
        Assert.IsNull(v);
    }

    [TestMethod]
    public async Task JsonIndex_GetAsync_HappyPath()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexGet", "{\"name\":\"alice\"}");
        var idx = NewJson(interop.Object);

        var result = await idx.GetAsync("a");
        Assert.IsTrue(result.TryGetSuccess(out var v));
        Assert.AreEqual("alice", v!.Value.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task JsonIndex_CountAsync_HappyPath()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexCount", "9");
        var idx = NewJson(interop.Object);

        var result = await idx.CountAsync();
        Assert.IsTrue(result.TryGetSuccess(out var n));
        Assert.AreEqual(9L, n);
    }

    [TestMethod]
    public async Task JsonIndex_GetAllKeysAsync_BadEnvelope_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("indexGetAllKeys", "[{\"bad\":true}]");
        var idx = NewJson(interop.Object);

        var result = await idx.GetAllKeysAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task JsonIndex_InactiveTx_AllOpsShortCircuit()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var idx = NewJson(interop.Object, active: false);

        Assert.IsTrue((await idx.GetAsync("a")).TryGetFailure(out _));
        Assert.IsTrue((await idx.GetAllKeysAsync()).TryGetFailure(out _));
        Assert.IsTrue((await idx.CountAsync()).TryGetFailure(out _));
        interop.Verify(x => x.InvokeRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }
}
