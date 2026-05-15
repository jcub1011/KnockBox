using System.Text.Json;
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
/// handles (db, cursor, blob), and the in-memory share registry. Each test
/// arranges a specific failure or cancellation path and asserts the matching
/// dispose / release call fires exactly once.
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
                capturedRef = (DotNetObjectReference<VersionChangeBridge>)args[4]!;
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
                capturedRef = (DotNetObjectReference<VersionChangeBridge>)args[4]!;
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
                capturedRef = (DotNetObjectReference<VersionChangeBridge>)args[4]!;
                return new ValueTask<ValueResult<OpenDatabaseResponse, IndexedDbError>>(
                    (ValueResult<OpenDatabaseResponse, IndexedDbError>)
                    new OpenDatabaseResponse(1, 1, Array.Empty<string>(), new Dictionary<string, StoreSchema>()));
            });
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var service = new IndexedDbService(interop.Object, NullLoggerFactory.Instance, registry);
        var open = await service.OpenAsync(new IndexedDbSchema("DB", 1));
        Assert.IsTrue(open.TryGetSuccess(out var db));
        // Before dispose: bridgeRef must still be live.
        Assert.IsNotNull(capturedRef);
        _ = capturedRef!.Value;
        await db.DisposeAsync();
        AssertDisposed(capturedRef);
    }

    // ──────────────────────────────────────────────────────────────────
    // IndexedDatabase.RunAsync — bridgeRef created on each call. Disposed
    // on every termination path: success, work error, abort failure,
    // commit failure, completed-faults, cancellation.
    // ──────────────────────────────────────────────────────────────────

    private static (Mock<IndexedDbInterop> interop, BlobShareRegistry registry, IndexedDatabase db, DotNetObjectReference<VersionChangeBridge> dbBridge)
        BuildDb()
    {
        var interop = Mock();
        interop.SetupVoidSuccess("closeDatabase");
        var registry = IndexedDbTestHelpers.NewRegistry();
        var schemaBridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, new IndexedDbSchema("DB", 1));
        var dbBridge = DotNetObjectReference.Create(schemaBridge);
        var db = new IndexedDatabase(
            interop.Object, NullLoggerFactory.Instance, registry,
            dbId: 1, name: "DB", version: 1,
            objectStoreNames: new[] { "things" },
            jsonOptions: new JsonSerializerOptions(),
            schema: new Dictionary<string, StoreSchema>(),
            bridgeRef: dbBridge);
        return (interop, registry, db, dbBridge);
    }

    [TestMethod]
    public async Task RunAsync_BeginTxFails_DisposesTxBridgeRef()
    {
        var (interop, registry, db, dbBridge) = BuildDb();
        DotNetObjectReference<TxCompletionBridge>? capturedTxBridge = null;
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedTxBridge = (DotNetObjectReference<TxCompletionBridge>)args[3]!;
                return new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                    new IndexedDbError(IndexedDbErrorKind.Blocked, "blocked"));
            });

        await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(
                ValueResult<int, IndexedDbError>.FromValue(0)));

        AssertDisposed(capturedTxBridge);
        await db.DisposeAsync();
        registry.Dispose();
    }

    [TestMethod]
    public async Task RunAsync_BeginTxCanceled_DisposesTxBridgeRef()
    {
        var (interop, registry, db, dbBridge) = BuildDb();
        DotNetObjectReference<TxCompletionBridge>? capturedTxBridge = null;
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedTxBridge = (DotNetObjectReference<TxCompletionBridge>)args[3]!;
                return new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                    ValueResult<BeginTransactionResponse, IndexedDbError>.Canceled);
            });

        await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(
                ValueResult<int, IndexedDbError>.FromValue(0)));

        AssertDisposed(capturedTxBridge);
        await db.DisposeAsync();
        registry.Dispose();
    }

    [TestMethod]
    public async Task RunAsync_WorkSucceeds_TxBridgeRefDisposed_AfterCompleted()
    {
        var (interop, registry, db, _) = BuildDb();
        interop.SetupTypedSuccess("beginTransaction", new BeginTransactionResponse(99));
        interop.SetupVoidSuccess("commitTransaction");
        DotNetObjectReference<TxCompletionBridge>? capturedTxBridge = null;
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedTxBridge = (DotNetObjectReference<TxCompletionBridge>)args[3]!;
                return new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                    (ValueResult<BeginTransactionResponse, IndexedDbError>)new BeginTransactionResponse(99));
            });

        await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) =>
            {
                var bridge = (TxCompletionBridge)typeof(IndexedDbTransaction)
                    .GetField("_bridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(tx)!;
                _ = Task.Run(() => bridge.OnComplete());
                return new ValueTask<ValueResult<int, IndexedDbError>>(
                    ValueResult<int, IndexedDbError>.FromValue(0));
            });

        AssertDisposed(capturedTxBridge);
        await db.DisposeAsync();
        registry.Dispose();
    }

    [TestMethod]
    public async Task RunAsync_WorkThrows_TxBridgeRefDisposed()
    {
        var (interop, registry, db, _) = BuildDb();
        DotNetObjectReference<TxCompletionBridge>? capturedTxBridge = null;
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedTxBridge = (DotNetObjectReference<TxCompletionBridge>)args[3]!;
                return new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                    (ValueResult<BeginTransactionResponse, IndexedDbError>)new BeginTransactionResponse(99));
            });
        interop.SetupVoidSuccess("abortTransaction");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await db.RunAsync<int>(
                new[] { "things" }, TransactionMode.ReadWrite,
                (tx, ct) => throw new InvalidOperationException("user bug")));

        AssertDisposed(capturedTxBridge);
        await db.DisposeAsync();
        registry.Dispose();
    }

    [TestMethod]
    public async Task RunAsync_CommitFails_TxBridgeRefDisposed()
    {
        var (interop, registry, db, _) = BuildDb();
        DotNetObjectReference<TxCompletionBridge>? capturedTxBridge = null;
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedTxBridge = (DotNetObjectReference<TxCompletionBridge>)args[3]!;
                return new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                    (ValueResult<BeginTransactionResponse, IndexedDbError>)new BeginTransactionResponse(99));
            });
        interop.SetupVoidFailure("commitTransaction",
            new IndexedDbError(IndexedDbErrorKind.QuotaExceeded, "full"));
        interop.SetupVoidSuccess("abortTransaction"); // belt-and-suspenders if Dispose calls it

        await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(
                ValueResult<int, IndexedDbError>.FromValue(0)));

        AssertDisposed(capturedTxBridge);
        await db.DisposeAsync();
        registry.Dispose();
    }

    [TestMethod]
    public async Task RunAsync_CompletedFaults_TxBridgeRefDisposed()
    {
        var (interop, registry, db, _) = BuildDb();
        DotNetObjectReference<TxCompletionBridge>? capturedTxBridge = null;
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns((string _, CancellationToken __, object?[] args) =>
            {
                capturedTxBridge = (DotNetObjectReference<TxCompletionBridge>)args[3]!;
                return new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                    (ValueResult<BeginTransactionResponse, IndexedDbError>)new BeginTransactionResponse(99));
            });
        interop.SetupVoidSuccess("commitTransaction");

        await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) =>
            {
                var bridge = (TxCompletionBridge)typeof(IndexedDbTransaction)
                    .GetField("_bridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(tx)!;
                _ = Task.Run(() => bridge.OnError("Aborted", "post-commit fault"));
                return new ValueTask<ValueResult<int, IndexedDbError>>(
                    ValueResult<int, IndexedDbError>.FromValue(0));
            });

        AssertDisposed(capturedTxBridge);
        await db.DisposeAsync();
        registry.Dispose();
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
    // Cursor handles — released on dispose. Empty cursors (CursorId=-1)
    // also fire release, but ReleaseAsync inside CursorRpc short-circuits
    // when the tx is inactive (which an empty-cursor caller may not hit).
    // ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CursorDispose_CallsReleaseHandle_ExactlyOnce()
    {
        var interop = Mock();
        var releaseCount = 0;
        interop.Setup(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref releaseCount);
                return new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success);
            });

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<int>(ctx, cursorId: 1, firstEntry: null);
        await cursor.DisposeAsync();
        await cursor.DisposeAsync();

        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    public async Task CursorDispose_AfterInactiveTx_SkipsRelease()
    {
        var interop = Mock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var cursor = new IndexedDbCursor<int>(ctx, 1, firstEntry: null);
        await cursor.DisposeAsync();
        interop.Verify(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────
    // BlobShareRegistry — published shares removed by Blob.DisposeAsync,
    // by share.DisposeAsync, by absolute expiry, and by sliding expiry
    // sweep. Verify every removal path.
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

        // No way to inspect registry size directly, but TryGetAndTouch on any
        // freshly-published token should now miss — pull entries via reflection
        // to avoid relying on internal state shape.
        var entries = (System.Collections.Concurrent.ConcurrentDictionary<Guid, BlobShareEntry>)typeof(BlobShareRegistry)
            .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(registry)!;
        Assert.AreEqual(0, entries.Count);
    }

    // ──────────────────────────────────────────────────────────────────
    // IndexedDbInterop — _moduleTask must be disposed on DisposeAsync,
    // but only if it was lazily created. Already covered in
    // IndexedDbInteropTests but repeated here for completeness.
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
