using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDatabaseTests
{
    private static IndexedDatabase NewDb(
        IndexedDbInterop interop,
        BlobShareRegistry registry,
        DotNetObjectReference<VersionChangeBridge> bridgeRef,
        int dbId = 1,
        params string[] storeNames)
        => new(
            interop,
            NullLoggerFactory.Instance,
            registry,
            dbId,
            name: "DB",
            version: 1,
            objectStoreNames: storeNames.Length == 0 ? new[] { "things" } : storeNames,
            jsonOptions: new JsonSerializerOptions(),
            schema: new Dictionary<string, StoreSchema>(),
            bridgeRef: bridgeRef);

    private static (VersionChangeBridge bridge, DotNetObjectReference<VersionChangeBridge> bridgeRef) NewBridge(IndexedDbInterop interop, BlobShareRegistry registry)
    {
        var bridge = new VersionChangeBridge(interop, NullLoggerFactory.Instance, registry, new IndexedDbSchema("DB", 1));
        return (bridge, DotNetObjectReference.Create(bridge));
    }

    [TestMethod]
    public async Task DisposeAsync_HappyPath_ClosesAndDisposesBridge()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        await db.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("closeDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = bridgeRef.Value);
    }

    [TestMethod]
    public async Task DisposeAsync_TolerantOfCloseFailure_StillDisposesBridge()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidFailure("closeDatabase", new IndexedDbError(IndexedDbErrorKind.Unknown, "weird"));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        await db.DisposeAsync();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = bridgeRef.Value);
    }

    [TestMethod]
    public async Task DisposeAsync_Idempotent()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        await db.DisposeAsync();
        await db.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("closeDatabase", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task RaiseVersionChangeRequestedAsync_InvokesAllSubscribers()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        int aCount = 0, bCount = 0;
        db.VersionChangeRequested += () => { aCount++; return ValueTask.CompletedTask; };
        db.VersionChangeRequested += () => { bCount++; return ValueTask.CompletedTask; };

        await db.RaiseVersionChangeRequestedAsync();

        Assert.AreEqual(1, aCount);
        Assert.AreEqual(1, bCount);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RaiseVersionChangeRequestedAsync_OneSubscriberThrows_OthersStillCalled()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var bCalled = false;
        db.VersionChangeRequested += () => throw new InvalidOperationException("subscriber bomb");
        db.VersionChangeRequested += () => { bCalled = true; return ValueTask.CompletedTask; };

        await db.RaiseVersionChangeRequestedAsync();

        Assert.IsTrue(bCalled, "second subscriber must run even when the first throws");
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RaiseVersionChangeRequestedAsync_NoSubscribers_DoesNothing()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        await db.RaiseVersionChangeRequestedAsync();
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RunAsync_Canceled_PropagatesAndDisposesBridge()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        interop.Setup(x => x.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                ValueResult<BeginTransactionResponse, IndexedDbError>.Canceled));

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var result = await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(
                ValueResult<int, IndexedDbError>.FromValue(0)));

        Assert.IsTrue(result.IsCanceled);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RunAsync_CtCanceled_DuringWork_AbortsAndReturnsCanceled()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        interop.SetupTypedSuccess("beginTransaction", new BeginTransactionResponse(99));
        interop.SetupVoidSuccess("abortTransaction");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await db.RunAsync<int>(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<ValueResult<int, IndexedDbError>>(
                    ValueResult<int, IndexedDbError>.FromValue(0));
            }, cts.Token);

        Assert.IsTrue(result.IsCanceled);
        interop.Verify(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RunAsync_NonGeneric_FailureWraps()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        interop.SetupTypedSuccess("beginTransaction", new BeginTransactionResponse(99));
        interop.SetupVoidSuccess("abortTransaction");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var err = new IndexedDbError(IndexedDbErrorKind.Constraint, "dup");
        var result = await db.RunAsync(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<Result<IndexedDbError>>(err));

        Assert.IsTrue(result.TryGetFailure(out var got));
        Assert.AreEqual(IndexedDbErrorKind.Constraint, got.Kind);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RunAsync_NonGeneric_HappyPath()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");
        interop.SetupTypedSuccess("beginTransaction", new BeginTransactionResponse(99));
        interop.SetupVoidSuccess("commitTransaction");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge(interop.Object, registry);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var result = await db.RunAsync(
            new[] { "things" }, TransactionMode.ReadWrite,
            (tx, ct) =>
            {
                var bridge = (TxCompletionBridge)typeof(IndexedDbTransaction)
                    .GetField("_bridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(tx)!;
                _ = Task.Run(() => bridge.OnComplete());
                return new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success);
            });

        Assert.IsTrue(result.IsSuccess);
        await db.DisposeAsync();
    }
}
