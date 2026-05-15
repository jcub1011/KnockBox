using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class BlobObjectStoreTests
{
    private static BlobObjectStore NewStore(IndexedDbInterop interop, BlobShareRegistry registry, IndexedDbTestHelpers.TestTxContext ctx)
        => new(ctx, NullLoggerFactory.Instance, registry, "blobs");

    private static IndexedDbBlobImpl NewBlob(IndexedDbInterop interop, BlobShareRegistry registry, int blobId = 1, long length = 16)
        => new(interop, NullLogger<IndexedDbBlobImpl>.Instance, registry, blobId, "image/png", length);

    [TestMethod]
    public async Task GetAsync_NullValue_ReturnsNullBlob()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("blobStoreGet", "null");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetSuccess(out var blob));
        Assert.IsNull(blob);
    }

    [TestMethod]
    public async Task GetAsync_HappyPath_ParsesBlob()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("blobStoreGet", "{\"blobId\":42,\"contentType\":\"image/jpeg\",\"length\":1024}");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetSuccess(out var blob));
        Assert.IsNotNull(blob);
        Assert.AreEqual(1024, blob.Length);
        Assert.AreEqual("image/jpeg", blob.ContentType);
    }

    [TestMethod]
    public async Task GetAsync_MissingContentType_DefaultsToOctetStream()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("blobStoreGet", "{\"blobId\":1,\"contentType\":null,\"length\":8}");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);

        var result = await store.GetAsync(1);
        Assert.IsTrue(result.TryGetSuccess(out var blob));
        Assert.AreEqual("application/octet-stream", blob!.ContentType);
    }

    [TestMethod]
    public async Task AddAsync_HappyPath_ReturnsKey()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("blobStoreAdd", IndexedDbTestHelpers.NumberKeyJson(99));
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);
        var blob = NewBlob(interop.Object, registry);

        var result = await store.AddAsync(blob);
        Assert.IsTrue(result.TryGetSuccess(out var key));
        Assert.AreEqual(99.0, (double)key.Value!);
    }

    [TestMethod]
    public async Task PutAsync_HappyPath_ReturnsKey()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("blobStorePut", IndexedDbTestHelpers.NumberKeyJson(100));
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);
        var blob = NewBlob(interop.Object, registry);

        var result = await store.PutAsync(blob);
        Assert.IsTrue(result.TryGetSuccess(out var key));
        Assert.AreEqual(100.0, (double)key.Value!);
    }

    [TestMethod]
    public async Task AddAsync_NonImplBlob_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);
        var foreignBlob = new ForeignBlob();

        var result = await store.AddAsync(foreignBlob);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
        interop.Verify(x => x.InvokeRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task PutAsync_MalformedKey_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawSuccess("blobStorePut", "{\"bogus\":1}");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);

        var result = await store.PutAsync(NewBlob(interop.Object, registry));
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task Inactive_AllOps_ShortCircuit()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var store = NewStore(interop.Object, registry, ctx);
        var blob = NewBlob(interop.Object, registry);

        var get = await store.GetAsync(1);
        var add = await store.AddAsync(blob);
        var put = await store.PutAsync(blob);
        var del = await store.DeleteAsync(1);
        var clr = await store.ClearAsync();
        var cnt = await store.CountAsync();
        var keys = await store.GetAllKeysAsync();
        var cur = await store.OpenCursorAsync();

        Assert.IsTrue(get.TryGetFailure(out _));
        Assert.IsTrue(add.TryGetFailure(out _));
        Assert.IsTrue(put.TryGetFailure(out _));
        Assert.IsTrue(del.TryGetFailure(out _));
        Assert.IsTrue(clr.TryGetFailure(out _));
        Assert.IsTrue(cnt.TryGetFailure(out _));
        Assert.IsTrue(keys.TryGetFailure(out _));
        Assert.IsTrue(cur.TryGetFailure(out _));
        interop.Verify(x => x.InvokeRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
        interop.Verify(x => x.InvokeAsync<CursorOpenResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task OpenCursorAsync_NoFirstEntry_ReturnsEmptyCursor()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedSuccess("openCursor", new CursorOpenResponse(CursorId: null, HasFirst: false, Entry: null));
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);

        var result = await store.OpenCursorAsync();
        Assert.IsTrue(result.TryGetSuccess(out var cursor));
        Assert.IsFalse(await cursor.MoveNextAsync());
        await cursor.DisposeAsync();
    }

    [TestMethod]
    public async Task OpenCursorAsync_Failure_ReturnsError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedFailure<CursorOpenResponse>("openCursor",
            new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "expired"));
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var store = NewStore(interop.Object, registry, ctx);

        var result = await store.OpenCursorAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, err.Kind);
    }

    private sealed class ForeignBlob : IndexedDbBlob
    {
        public override string ContentType => "x/y";
        public override long Length => 0;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => ValueTask.FromResult(Array.Empty<byte>());
        public override ValueTask<Stream> OpenReadAsync(CancellationToken ct = default) => ValueTask.FromResult<Stream>(Stream.Null);
        public override ValueTask<string> CreateObjectUrlAsync(CancellationToken ct = default) => ValueTask.FromResult("");
        public override ValueTask<IBlobShare> PublishForSharingAsync(BlobShareOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
