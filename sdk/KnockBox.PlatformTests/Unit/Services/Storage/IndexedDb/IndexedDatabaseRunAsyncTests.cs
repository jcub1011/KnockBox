using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDatabaseRunAsyncTests
{
    private static Mock<IndexedDbInterop> NewInteropMock()
        => new(new Mock<IJSRuntime>().Object, NullLogger<IndexedDbInterop>.Instance) { CallBase = false };

    private static IndexedDatabase NewDb(IndexedDbInterop interop, BlobShareRegistry registry, DotNetObjectReference<VersionChangeBridge> bridgeRef)
        => new(
            interop,
            NullLoggerFactory.Instance,
            registry,
            dbId: 1,
            name: "TestDb",
            version: 1,
            objectStoreNames: new[] { "things" },
            jsonOptions: new JsonSerializerOptions(),
            schema: new Dictionary<string, StoreSchema>(),
            bridgeRef: bridgeRef);

    private static void SetupBeginTx(Mock<IndexedDbInterop> interop, int txId)
    {
        interop
            .Setup(x => x.InvokeAsync<BeginTransactionResponse>(
                "beginTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<BeginTransactionResponse, IndexedDbError>>(
                (ValueResult<BeginTransactionResponse, IndexedDbError>)new BeginTransactionResponse(txId)));
    }

    [TestMethod]
    public async Task SuccessfulWork_CommitsAndAwaitsCompleted()
    {
        var interop = NewInteropMock();
        SetupBeginTx(interop, 42);
        interop.Setup(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);
        using var bridgeRef = DotNetObjectReference.Create(bridge);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var result = await db.RunAsync<int>(
            new[] { "things" },
            TransactionMode.ReadWrite,
            async (tx, ct) =>
            {
                // Simulate JS-side oncomplete arriving by signaling the bridge
                // after commit. We capture the internal completion bridge by
                // poking the transaction's Completed task. Simpler: fire on a
                // background task.
                _ = Task.Run(() =>
                {
                    var bridgeField = typeof(IndexedDbTransaction)
                        .GetField("_bridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    var txBridge = (TxCompletionBridge)bridgeField.GetValue(tx)!;
                    txBridge.OnComplete();
                });
                return ValueResult<int, IndexedDbError>.FromValue(7);
            });

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.TryGetSuccess(out var value));
        Assert.AreEqual(7, value);
    }

    [TestMethod]
    public async Task FailureFromWork_AbortsAndReturnsError()
    {
        var interop = NewInteropMock();
        SetupBeginTx(interop, 42);
        interop.Setup(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);
        using var bridgeRef = DotNetObjectReference.Create(bridge);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var workError = new IndexedDbError(IndexedDbErrorKind.Constraint, "duplicate");
        var result = await db.RunAsync<int>(
            new[] { "things" },
            TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(workError));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Constraint, err.Kind);
        interop.Verify(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
        interop.Verify(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task ExceptionFromWork_AbortsAndRethrows()
    {
        var interop = NewInteropMock();
        SetupBeginTx(interop, 42);
        interop.Setup(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);
        using var bridgeRef = DotNetObjectReference.Create(bridge);
        var db = NewDb(interop.Object, registry, bridgeRef);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await db.RunAsync<int>(
                new[] { "things" },
                TransactionMode.ReadWrite,
                (tx, ct) => throw new InvalidOperationException("oops")));

        interop.Verify(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task CommitFailure_PropagatesError()
    {
        var interop = NewInteropMock();
        SetupBeginTx(interop, 42);
        interop.Setup(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.QuotaExceeded, "full")));
        interop.Setup(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);
        using var bridgeRef = DotNetObjectReference.Create(bridge);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var result = await db.RunAsync<int>(
            new[] { "things" },
            TransactionMode.ReadWrite,
            (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(
                ValueResult<int, IndexedDbError>.FromValue(0)));

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.QuotaExceeded, err.Kind);
    }

    [TestMethod]
    public async Task CompletedFaultsAfterCommit_ReturnsEmbeddedError()
    {
        var interop = NewInteropMock();
        SetupBeginTx(interop, 42);
        interop.Setup(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);
        using var bridgeRef = DotNetObjectReference.Create(bridge);
        var db = NewDb(interop.Object, registry, bridgeRef);

        var result = await db.RunAsync<int>(
            new[] { "things" },
            TransactionMode.ReadWrite,
            (tx, ct) =>
            {
                var bridgeField = typeof(IndexedDbTransaction)
                    .GetField("_bridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var txBridge = (TxCompletionBridge)bridgeField.GetValue(tx)!;
                _ = Task.Run(() => txBridge.OnError("Aborted", "post-commit fault"));
                return new ValueTask<ValueResult<int, IndexedDbError>>(
                    ValueResult<int, IndexedDbError>.FromValue(11));
            });

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Aborted, err.Kind);
    }

    [TestMethod]
    public async Task EmptyStoreNames_Throws()
    {
        var interop = NewInteropMock();
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);
        using var bridgeRef = DotNetObjectReference.Create(bridge);
        var db = NewDb(interop.Object, registry, bridgeRef);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await db.RunAsync<int>(
                Array.Empty<string>(),
                TransactionMode.ReadOnly,
                (tx, ct) => new ValueTask<ValueResult<int, IndexedDbError>>(
                    ValueResult<int, IndexedDbError>.FromValue(0))));
    }
}
