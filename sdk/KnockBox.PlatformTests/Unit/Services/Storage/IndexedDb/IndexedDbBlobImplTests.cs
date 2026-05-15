using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbBlobImplTests
{
    private static Mock<IndexedDbInterop> NewInteropMock()
    {
        var jsRuntime = new Mock<IJSRuntime>();
        var logger = NullLogger<IndexedDbInterop>.Instance;
        return new Mock<IndexedDbInterop>(jsRuntime.Object, logger) { CallBase = false };
    }

    private static IndexedDbBlobImpl NewBlob(IndexedDbInterop interop, BlobShareRegistry registry, int blobId = 1, long length = 32, string contentType = "application/octet-stream")
        => new(interop, NullLogger<IndexedDbBlobImpl>.Instance, registry, blobId, contentType, length);

    [TestMethod]
    public async Task EnsureReadPreparedAsync_ConcurrentCallers_InvokeJsOnce()
    {
        var interop = NewInteropMock();
        var prepareCallCount = 0;
        var gate = new TaskCompletionSource();

        interop
            .Setup(x => x.InvokeAsync<BlobPrepareReadResponse>(
                "blobPrepareRead", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref prepareCallCount);
                await gate.Task;
                return (ValueResult<BlobPrepareReadResponse, IndexedDbError>)
                    new BlobPrepareReadResponse(8, "application/octet-stream");
            });
        interop
            .Setup(x => x.InvokeAsync<BlobChunkResponse>(
                "blobReadChunk", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobChunkResponse, IndexedDbError>>(
                (ValueResult<BlobChunkResponse, IndexedDbError>)new BlobChunkResponse(Convert.ToBase64String(new byte[8]))));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry, length: 8);

        // Kick off three concurrent reads. Each calls EnsureReadPreparedAsync.
        var t1 = blob.ReadAllBytesAsync().AsTask();
        var t2 = blob.ReadAllBytesAsync().AsTask();
        var t3 = blob.ReadAllBytesAsync().AsTask();

        // All three are awaiting the same gated prepare. Release it.
        gate.SetResult();
        await Task.WhenAll(t1, t2, t3);

        Assert.AreEqual(1, prepareCallCount,
            "concurrent callers must share a single blobPrepareRead invocation");
    }

    [TestMethod]
    public async Task DisposeAsync_RevokesPublishedShares()
    {
        var interop = NewInteropMock();
        interop
            .Setup(x => x.InvokeAsync<BlobPrepareReadResponse>(
                "blobPrepareRead", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobPrepareReadResponse, IndexedDbError>>(
                (ValueResult<BlobPrepareReadResponse, IndexedDbError>)
                    new BlobPrepareReadResponse(4, "application/octet-stream")));
        interop
            .Setup(x => x.InvokeVoidAsync(
                "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry, length: 4);

        var share1 = await blob.PublishForSharingAsync();
        var share2 = await blob.PublishForSharingAsync();

        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share1.Url["/blob-share/".Length..])));
        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share2.Url["/blob-share/".Length..])));

        await blob.DisposeAsync();

        Assert.IsNull(registry.TryGetAndTouch(Guid.Parse(share1.Url["/blob-share/".Length..])));
        Assert.IsNull(registry.TryGetAndTouch(Guid.Parse(share2.Url["/blob-share/".Length..])));
    }

    [TestMethod]
    public async Task ReadAllBytesAsync_ZeroByteChunk_ThrowsBeforeAdvancingOffset()
    {
        var interop = NewInteropMock();
        interop
            .Setup(x => x.InvokeAsync<BlobPrepareReadResponse>(
                "blobPrepareRead", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobPrepareReadResponse, IndexedDbError>>(
                (ValueResult<BlobPrepareReadResponse, IndexedDbError>)
                    new BlobPrepareReadResponse(16, "application/octet-stream")));
        interop
            .Setup(x => x.InvokeAsync<BlobChunkResponse>(
                "blobReadChunk", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobChunkResponse, IndexedDbError>>(
                (ValueResult<BlobChunkResponse, IndexedDbError>)new BlobChunkResponse(Convert.ToBase64String(Array.Empty<byte>()))));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry, length: 16);

        var ex = await Assert.ThrowsExactlyAsync<IOException>(
            async () => await blob.ReadAllBytesAsync());

        StringAssert.Contains(ex.Message, "at offset 0");
    }

    [TestMethod]
    public async Task DisposedBlob_ReadAllBytes_Throws()
    {
        var interop = NewInteropMock();
        interop
            .Setup(x => x.InvokeVoidAsync(
                "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await blob.ReadAllBytesAsync());
    }

    [TestMethod]
    public async Task DisposedBlob_PublishForSharing_Throws()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await blob.PublishForSharingAsync());
    }

    [TestMethod]
    public async Task DisposedBlob_CreateObjectUrl_Throws()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await blob.CreateObjectUrlAsync());
    }

    [TestMethod]
    public async Task DisposedBlob_OpenRead_Throws()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await blob.OpenReadAsync());
    }

    [TestMethod]
    public async Task CreateObjectUrlAsync_HappyPath_CachesAcrossCalls()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobUrlResponse, IndexedDbError>>(
                (ValueResult<BlobUrlResponse, IndexedDbError>)new BlobUrlResponse("blob:abc")));
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        var url1 = await blob.CreateObjectUrlAsync();
        var url2 = await blob.CreateObjectUrlAsync();
        Assert.AreEqual("blob:abc", url1);
        Assert.AreEqual(url1, url2);
        interop.Verify(x => x.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);

        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateObjectUrlAsync_Failure_ThrowsIOException()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobUrlResponse, IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.Aborted, "circuit gone")));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        var ex = await Assert.ThrowsExactlyAsync<IOException>(async () => await blob.CreateObjectUrlAsync());
        StringAssert.Contains(ex.Message, "blobCreateObjectUrl");
    }

    [TestMethod]
    public async Task PrepareReadFailure_IsCached_SubsequentReadsSeeSameError()
    {
        var interop = NewInteropMock();
        var prepareCount = 0;
        interop.Setup(x => x.InvokeAsync<BlobPrepareReadResponse>(
            "blobPrepareRead", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref prepareCount);
                return new ValueTask<ValueResult<BlobPrepareReadResponse, IndexedDbError>>(
                    new IndexedDbError(IndexedDbErrorKind.Aborted, "circuit gone"));
            });

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        await Assert.ThrowsExactlyAsync<IOException>(async () => await blob.ReadAllBytesAsync());
        await Assert.ThrowsExactlyAsync<IOException>(async () => await blob.ReadAllBytesAsync());
        // Both reads share the same failed prepare task — JS only saw it once.
        Assert.AreEqual(1, prepareCount);
    }

    [TestMethod]
    public async Task PublishForSharingAsync_AbsoluteExpiry_StoredCorrectly()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobPrepareReadResponse>(
            "blobPrepareRead", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobPrepareReadResponse, IndexedDbError>>(
                (ValueResult<BlobPrepareReadResponse, IndexedDbError>)new BlobPrepareReadResponse(4, "application/octet-stream")));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        var share = await blob.PublishForSharingAsync(new BlobShareOptions
        {
            AbsoluteExpiry = TimeSpan.FromMinutes(5),
            SlidingExpiry = TimeSpan.FromMinutes(1),
            CacheControl = "public, max-age=300",
        });

        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share.Url["/blob-share/".Length..])));
        await share.DisposeAsync();
        Assert.IsNull(registry.TryGetAndTouch(Guid.Parse(share.Url["/blob-share/".Length..])));
    }

    [TestMethod]
    public async Task DisposeAsync_Idempotent_OnlyCallsReleaseOnce()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        await blob.DisposeAsync();
        await blob.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_TolerantOfReleaseFailure()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.Aborted, "circuit gone")));
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry);

        // Must not throw.
        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task OpenReadAsync_AfterPrepare_ReturnsStream()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobPrepareReadResponse>(
            "blobPrepareRead", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobPrepareReadResponse, IndexedDbError>>(
                (ValueResult<BlobPrepareReadResponse, IndexedDbError>)new BlobPrepareReadResponse(4, "application/octet-stream")));
        interop.Setup(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var blob = NewBlob(interop.Object, registry, length: 4);

        await using var stream = await blob.OpenReadAsync();
        Assert.IsNotNull(stream);
        Assert.AreEqual(4, stream.Length);

        await blob.DisposeAsync();
    }
}
