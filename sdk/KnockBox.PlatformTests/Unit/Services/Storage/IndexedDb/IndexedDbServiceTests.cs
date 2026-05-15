using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbServiceTests
{
    private static IndexedDbService NewService(IndexedDbInterop interop, BlobShareRegistry registry)
        => new(interop, NullLoggerFactory.Instance, registry);

    private static OpenDatabaseResponse OpenSuccess(int dbId = 1, int version = 1, params string[] stores)
        => new(dbId, version, stores, new Dictionary<string, StoreSchema>());

    [TestMethod]
    public async Task OpenAsync_HappyPath_ReturnsDatabase()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedSuccess("openDatabase", OpenSuccess(dbId: 7, version: 1, stores: "users"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.OpenAsync(new IndexedDbSchema("DB", 1));

        Assert.IsTrue(result.TryGetSuccess(out var db));
        Assert.AreEqual("DB", db.Name);
        Assert.AreEqual(1, db.Version);
        CollectionAssert.AreEqual(new[] { "users" }, db.ObjectStoreNames.ToArray());

        // Cleanup so closeDatabase doesn't throw.
        interop.SetupVoidSuccess("closeDatabase");
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task OpenAsync_Failure_DisposesBridgeRef_NoLeak()
    {
        // We verify no-leak indirectly: the same bridge GUID never reappears
        // in subsequent JSInvokable calls (a leaked DotNetObjectReference would
        // keep the bridge alive). Direct observation: bridgeRef.Dispose() flips
        // its Value getter to throw — track via reflection on the mock callback.
        var interop = IndexedDbTestHelpers.NewInteropMock();
        DotNetObjectReference<VersionChangeBridge>? capturedBridgeRef = null;
        interop
            .Setup(x => x.InvokeAsync<OpenDatabaseResponse>(
                "openDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedBridgeRef = (DotNetObjectReference<VersionChangeBridge>)args[4]!;
                return new ValueTask<ValueResult<OpenDatabaseResponse, IndexedDbError>>(
                    new IndexedDbError(IndexedDbErrorKind.Version, "version mismatch"));
            });

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.OpenAsync(new IndexedDbSchema("DB", 1));

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Version, err.Kind);
        Assert.IsNotNull(capturedBridgeRef);
        // Accessing Value on a disposed DotNetObjectReference throws ObjectDisposedException.
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = capturedBridgeRef!.Value);
    }

    [TestMethod]
    public async Task OpenAsync_Canceled_ReturnsCanceled_AndDisposesBridgeRef()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        DotNetObjectReference<VersionChangeBridge>? capturedBridgeRef = null;
        interop
            .Setup(x => x.InvokeAsync<OpenDatabaseResponse>(
                "openDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedBridgeRef = (DotNetObjectReference<VersionChangeBridge>)args[4]!;
                return new ValueTask<ValueResult<OpenDatabaseResponse, IndexedDbError>>(
                    ValueResult<OpenDatabaseResponse, IndexedDbError>.Canceled);
            });

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.OpenAsync(new IndexedDbSchema("DB", 1));

        Assert.IsTrue(result.IsCanceled);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = capturedBridgeRef!.Value);
    }

    [TestMethod]
    public async Task DeleteDatabaseAsync_HappyPath_ReturnsSuccess()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("deleteDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.DeleteDatabaseAsync("DB");
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task DeleteDatabaseAsync_Failure_ReturnsError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidFailure("deleteDatabase",
            new IndexedDbError(IndexedDbErrorKind.Blocked, "another tab"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.DeleteDatabaseAsync("DB");
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Blocked, err.Kind);
    }

    [TestMethod]
    public async Task ListDatabasesAsync_HappyPath_ReturnsInfos()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var resp = new ListDatabasesResponse(new[]
        {
            new DatabaseInfoEntry("DB1", 2),
            new DatabaseInfoEntry("DB2", 5),
        });
        interop.SetupTypedSuccess("listDatabases", resp);

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.ListDatabasesAsync();
        Assert.IsTrue(result.TryGetSuccess(out var infos));
        Assert.AreEqual(2, infos.Count);
        Assert.AreEqual("DB1", infos[0].Name);
        Assert.AreEqual(2, infos[0].Version);
    }

    [TestMethod]
    public async Task ListDatabasesAsync_NotSupported_ReturnsError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedFailure<ListDatabasesResponse>("listDatabases",
            new IndexedDbError(IndexedDbErrorKind.NotSupported, "old browser"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var result = await service.ListDatabasesAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.NotSupported, err.Kind);
    }

    [TestMethod]
    public async Task CreateBlobAsync_Bytes_Small_UsesSingleCall()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedSuccess("createBlobFromBytes", new BlobCreateResponse(BlobId: 1, Length: 5));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        interop.SetupVoidSuccess("releaseHandle");

        var blob = await service.CreateBlobAsync(bytes, "application/octet-stream");

        Assert.AreEqual(5, blob.Length);
        Assert.AreEqual("application/octet-stream", blob.ContentType);
        interop.Verify(x => x.InvokeAsync<BlobCreateResponse>(
            "createBlobFromBytes", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
        interop.Verify(x => x.InvokeAsync<BlobStreamBeginResponse>(
            "createBlobStreamBegin", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);

        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateBlobAsync_Bytes_Small_FailureThrowsIOException()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedFailure<BlobCreateResponse>("createBlobFromBytes",
            new IndexedDbError(IndexedDbErrorKind.QuotaExceeded, "full"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await service.CreateBlobAsync(new byte[] { 1, 2 }, "application/octet-stream"));
    }

    [TestMethod]
    public async Task CreateBlobAsync_Bytes_LargerThanChunk_UsesStreamPath()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var len = IndexedDbBlobChunking.ChunkSize + 1;
        interop.SetupTypedSuccess("createBlobStreamBegin", new BlobStreamBeginResponse(UploadId: 1));
        interop.SetupVoidSuccess("createBlobStreamAppend");
        interop.SetupTypedSuccess("createBlobStreamFinish", new BlobCreateResponse(BlobId: 7, Length: len));
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var blob = await service.CreateBlobAsync(new byte[len], "application/octet-stream");

        Assert.AreEqual(len, blob.Length);
        // Two chunk appends: one full, one of length 1.
        interop.Verify(x => x.InvokeVoidAsync(
            "createBlobStreamAppend", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Exactly(2));

        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateBlobAsync_Stream_NonReadable_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var stream = new MemoryStream(new byte[] { 1, 2 }, writable: false);
        stream.Close(); // makes CanRead false

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await service.CreateBlobAsync(stream, length: 2, contentType: "application/octet-stream"));
        StringAssert.Contains(ex.Message, "readable");
    }

    [TestMethod]
    public async Task CreateBlobAsync_Stream_NegativeLength_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.CreateBlobAsync(new MemoryStream(), length: -1, "application/octet-stream"));
    }

    [TestMethod]
    public async Task CreateBlobAsync_Stream_DisposesStreamByDefault()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedSuccess("createBlobStreamBegin", new BlobStreamBeginResponse(UploadId: 1));
        interop.SetupVoidSuccess("createBlobStreamAppend");
        interop.SetupTypedSuccess("createBlobStreamFinish", new BlobCreateResponse(BlobId: 7, Length: 3));
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var stream = new TrackingStream(new byte[] { 1, 2, 3 });
        var blob = await service.CreateBlobAsync(stream, length: 3, "application/octet-stream");

        Assert.IsTrue(stream.WasDisposed);
        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateBlobAsync_Stream_LeaveOpenTrue_DoesNotDispose()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedSuccess("createBlobStreamBegin", new BlobStreamBeginResponse(UploadId: 1));
        interop.SetupVoidSuccess("createBlobStreamAppend");
        interop.SetupTypedSuccess("createBlobStreamFinish", new BlobCreateResponse(BlobId: 7, Length: 3));
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var stream = new TrackingStream(new byte[] { 1, 2, 3 });
        var blob = await service.CreateBlobAsync(stream, length: 3, "application/octet-stream", leaveOpen: true);

        Assert.IsFalse(stream.WasDisposed);
        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateBlobAsync_Stream_BeginFailure_StillDisposesStream()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedFailure<BlobStreamBeginResponse>("createBlobStreamBegin",
            new IndexedDbError(IndexedDbErrorKind.QuotaExceeded, "no room"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        var stream = new TrackingStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await service.CreateBlobAsync(stream, length: 3, "application/octet-stream"));
        Assert.IsTrue(stream.WasDisposed,
            "stream must be disposed in the finally block even when the JS begin call fails");
    }

    [TestMethod]
    public async Task CreateBlobAsync_Stream_TruncatedSource_ThrowsAndDisposesStream()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupTypedSuccess("createBlobStreamBegin", new BlobStreamBeginResponse(UploadId: 1));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);

        // Claim length 5 but only provide 2 bytes. Loop will see read==0 on
        // the second iteration and throw.
        var stream = new TrackingStream(new byte[] { 1, 2 });
        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await service.CreateBlobAsync(stream, length: 5, "application/octet-stream"));
        Assert.IsTrue(stream.WasDisposed);
    }

    [TestMethod]
    public async Task DisposeAsync_DisposesInterop()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var disposeCount = 0;
        interop.Setup(x => x.DisposeAsync()).Returns(() => { disposeCount++; return ValueTask.CompletedTask; });

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = NewService(interop.Object, registry);
        await service.DisposeAsync();

        Assert.AreEqual(1, disposeCount);
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool WasDisposed { get; private set; }
        public TrackingStream(byte[] payload) : base(payload, writable: false) { }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { WasDisposed = true; return base.DisposeAsync(); }
    }
}
