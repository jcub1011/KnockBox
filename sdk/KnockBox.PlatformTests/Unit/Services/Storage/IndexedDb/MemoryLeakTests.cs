using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

/// <summary>
/// Targeted regression coverage for every allocation that could leak across
/// the C#/JS boundary: DotNetObjectReferences, IJSObjectReferences, JS-side
/// handles (db, blob), and the in-memory share registry. Each test arranges
/// a specific failure or cancellation path and asserts the matching dispose
/// / release call fires exactly once.
/// </summary>
[TestClass]
public sealed class MemoryLeakTests
{
    private static Mock<IndexedDbInterop> Mock() => IndexedDbTestHelpers.NewInteropMock();

    // ──────────────────────────────────────────────────────────────────
    // IndexedDbService.OpenAsync — bridgeRef must be disposed on every
    // failure mode of openDatabase. (Happy path hands ownership to the
    // returned IndexedDatabase which disposes it on its own DisposeAsync.)
    // ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OpenAsync_FailureWithError_DisposesBridgeRef()
    {
        var interop = Mock();
        DotNetObjectReference<VersionChangeBridge>? capturedRef = null;
        interop.Setup(x => x.InvokeAsync<OpenDatabaseResponse>(
            "openDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedRef = (DotNetObjectReference<VersionChangeBridge>)args[3]!;
                return new ValueTask<ValueResult<OpenDatabaseResponse, IndexedDbError>>(
                    new IndexedDbError(IndexedDbErrorKind.Version, "vc"));
            });

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = new IndexedDbService(interop.Object, NullLoggerFactory.Instance, registry);
        await service.OpenAsync(new IndexedDbSchema("DB", 1));

        AssertDisposed(capturedRef);
    }

    [TestMethod]
    public async Task OpenAsync_FailureWithCancel_DisposesBridgeRef()
    {
        var interop = Mock();
        DotNetObjectReference<VersionChangeBridge>? capturedRef = null;
        interop.Setup(x => x.InvokeAsync<OpenDatabaseResponse>(
            "openDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedRef = (DotNetObjectReference<VersionChangeBridge>)args[3]!;
                return new ValueTask<ValueResult<OpenDatabaseResponse, IndexedDbError>>(
                    ValueResult<OpenDatabaseResponse, IndexedDbError>.Canceled);
            });

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = new IndexedDbService(interop.Object, NullLoggerFactory.Instance, registry);
        await service.OpenAsync(new IndexedDbSchema("DB", 1));

        AssertDisposed(capturedRef);
    }

    [TestMethod]
    public async Task OpenAsync_Success_DisposesBridgeRef_OnDatabaseDispose()
    {
        var interop = Mock();
        DotNetObjectReference<VersionChangeBridge>? capturedRef = null;
        interop.Setup(x => x.InvokeAsync<OpenDatabaseResponse>(
            "openDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedRef = (DotNetObjectReference<VersionChangeBridge>)args[3]!;
                return new ValueTask<ValueResult<OpenDatabaseResponse, IndexedDbError>>(
                    (ValueResult<OpenDatabaseResponse, IndexedDbError>)
                    new OpenDatabaseResponse(1, 1, Array.Empty<string>()));
            });
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = new IndexedDbService(interop.Object, NullLoggerFactory.Instance, registry);
        var open = await service.OpenAsync(new IndexedDbSchema("DB", 1));
        Assert.IsTrue(open.TryGetSuccess(out var db));
        Assert.IsNotNull(capturedRef);
        _ = capturedRef!.Value;
        await db.DisposeAsync();
        AssertDisposed(capturedRef);
    }

    // ──────────────────────────────────────────────────────────────────
    // IndexedDbBlobImpl — JS handle release on every dispose path,
    // plus published shares revoked even when the JS release fails.
    // ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BlobDispose_CallsReleaseHandle_ExactlyOnce()
    {
        var interop = Mock();
        var releaseCount = 0;
        interop.Setup(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref releaseCount);
                return new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success);
            });

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = new IndexedDbBlobImpl(interop.Object, NullLogger<IndexedDbBlobImpl>.Instance, registry, 1, "image/png", 8);

        await blob.DisposeAsync();
        await blob.DisposeAsync();
        await blob.DisposeAsync();

        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    public async Task BlobDispose_RevokesAllPublishedShares_EvenIfReleaseFails()
    {
        var interop = Mock();
        interop.SetupTypedSuccess("blobPrepareRead", new BlobPrepareReadResponse(4, "application/octet-stream"));
        interop.SetupVoidFailure("releaseHandle", new IndexedDbError(IndexedDbErrorKind.Aborted, "disconnected"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = new IndexedDbBlobImpl(interop.Object, NullLogger<IndexedDbBlobImpl>.Instance, registry, 1, "application/octet-stream", 4);

        var s1 = await blob.PublishForSharingAsync();
        var s2 = await blob.PublishForSharingAsync();
        var s3 = await blob.PublishForSharingAsync();

        await blob.DisposeAsync();

        Assert.IsNull(registry.TryGetAndTouch(s1.Token));
        Assert.IsNull(registry.TryGetAndTouch(s2.Token));
        Assert.IsNull(registry.TryGetAndTouch(s3.Token));
    }

    // ──────────────────────────────────────────────────────────────────
    // BlobShareRegistry — every published share is revoked when the
    // owning blob is disposed.
    // ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ShareLifecycle_BlobDisposeRevokes_NoLingeringEntries()
    {
        var interop = Mock();
        interop.SetupTypedSuccess("blobPrepareRead", new BlobPrepareReadResponse(4, "application/octet-stream"));
        interop.SetupVoidSuccess("releaseHandle");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var blob = new IndexedDbBlobImpl(interop.Object, NullLogger<IndexedDbBlobImpl>.Instance, registry, 1, "application/octet-stream", 4);

        for (var i = 0; i < 10; i++) await blob.PublishForSharingAsync();
        await blob.DisposeAsync();

        var entries = (System.Collections.Concurrent.ConcurrentDictionary<Guid, BlobShareEntry>)typeof(BlobShareRegistry)
            .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(registry)!;
        Assert.AreEqual(0, entries.Count);
    }

    // ──────────────────────────────────────────────────────────────────
    // IndexedDbInterop — _moduleTask must be disposed on DisposeAsync,
    // but only if it was lazily created.
    // ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Interop_NeverUsed_DoesNotImportOnDispose()
    {
        var rt = new Mock<IJSRuntime>();
        var interop = new IndexedDbInterop(rt.Object, NullLogger<IndexedDbInterop>.Instance);
        await interop.DisposeAsync();
        rt.Verify(x => x.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]>()), Times.Never);
    }

    private static void AssertDisposed<T>(DotNetObjectReference<T>? r) where T : class
    {
        Assert.IsNotNull(r);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = r!.Value);
    }
}
