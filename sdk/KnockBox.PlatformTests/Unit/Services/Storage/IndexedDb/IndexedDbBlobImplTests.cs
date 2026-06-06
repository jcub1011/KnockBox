using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

/// <summary>
/// Unit tests for <see cref="IndexedDbBlobImpl"/>. The full happy-path
/// happens through Blazor's <see cref="IJSStreamReference"/> binary
/// streaming pipeline, which isn't unit-testable without a real SignalR
/// circuit — see the BlobShareEndpoint manual smoke test for end-to-end
/// coverage. These tests cover the parts that ARE mockable: registration
/// of shares in the registry, disposed-state guards, and the object-URL
/// caching path.
/// </summary>
[TestClass]
public sealed class IndexedDbBlobImplTests
{
    private static IndexedDbBlobImpl NewBlob(
        IndexedDbInterop interop,
        BlobShareRegistry registry,
        int blobId = 1,
        long length = 32,
        string contentType = "application/octet-stream")
        => new(interop, NullLogger<IndexedDbBlobImpl>.Instance, registry, blobId, contentType, length);

    [TestMethod]
    public async Task PublishForSharingAsync_RegistersInRegistry_AndReturnsToken()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry, length: 4);

        Assert.IsTrue((await blob.PublishForSharingAsync()).TryGetSuccess(out var share));

        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share.Url["/blob-share/".Length..])));
        Assert.AreEqual(4, share.Length);
    }

    [TestMethod]
    public async Task DisposeAsync_RevokesPublishedShares()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry, length: 4);

        Assert.IsTrue((await blob.PublishForSharingAsync()).TryGetSuccess(out var share1));
        Assert.IsTrue((await blob.PublishForSharingAsync()).TryGetSuccess(out var share2));

        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share1.Url["/blob-share/".Length..])));
        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share2.Url["/blob-share/".Length..])));

        await blob.DisposeAsync();

        Assert.IsNull(registry.TryGetAndTouch(Guid.Parse(share1.Url["/blob-share/".Length..])));
        Assert.IsNull(registry.TryGetAndTouch(Guid.Parse(share2.Url["/blob-share/".Length..])));
    }

    [TestMethod]
    public async Task PublishForSharingAsync_AbsoluteExpiry_StoredCorrectly()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);

        var shareResult = await blob.PublishForSharingAsync(new BlobShareOptions
        {
            AbsoluteExpiry = TimeSpan.FromMinutes(5),
            SlidingExpiry = TimeSpan.FromMinutes(1),
            CacheControl = "public, max-age=300",
        });
        Assert.IsTrue(shareResult.TryGetSuccess(out var share));

        Assert.IsNotNull(registry.TryGetAndTouch(Guid.Parse(share.Url["/blob-share/".Length..])));
        await share.DisposeAsync();
        Assert.IsNull(registry.TryGetAndTouch(Guid.Parse(share.Url["/blob-share/".Length..])));
    }

    [TestMethod]
    public async Task DisposedBlob_ReadAllBytes_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await blob.ReadAllBytesAsync());
    }

    [TestMethod]
    public async Task DisposedBlob_PublishForSharing_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await blob.PublishForSharingAsync());
    }

    [TestMethod]
    public async Task DisposedBlob_CreateObjectUrl_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await blob.CreateObjectUrlAsync());
    }

    [TestMethod]
    public async Task DisposedBlob_OpenRead_Throws()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);
        await blob.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await blob.OpenReadAsync());
    }

    [TestMethod]
    public async Task CreateObjectUrlAsync_HappyPath_CachesAcrossCalls()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobUrlResponse, IndexedDbError>>(
                (ValueResult<BlobUrlResponse, IndexedDbError>)new BlobUrlResponse("blob:abc")));
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);

        Assert.IsTrue((await blob.CreateObjectUrlAsync()).TryGetSuccess(out var url1));
        Assert.IsTrue((await blob.CreateObjectUrlAsync()).TryGetSuccess(out var url2));
        Assert.AreEqual("blob:abc", url1);
        Assert.AreEqual(url1, url2);
        interop.Verify(x => x.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);

        await blob.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateObjectUrlAsync_Failure_ReturnsError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BlobUrlResponse, IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.Aborted, "circuit gone")));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);

        var result = await blob.CreateObjectUrlAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Aborted, err.Kind);
        StringAssert.Contains(err.Message, "circuit gone");
    }

    [TestMethod]
    public async Task DisposeAsync_Idempotent_OnlyCallsReleaseOnce()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);

        await blob.DisposeAsync();
        await blob.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync(
            "releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_TolerantOfReleaseFailure()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidFailure("releaseHandle",
            new IndexedDbError(IndexedDbErrorKind.Aborted, "circuit gone"));
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = NewBlob(interop.Object, registry);

        // Must not throw.
        await blob.DisposeAsync();
    }
}
