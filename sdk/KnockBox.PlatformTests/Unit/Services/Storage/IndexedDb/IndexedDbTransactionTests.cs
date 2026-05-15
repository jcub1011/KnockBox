using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbTransactionTests
{
    private static Mock<IndexedDbInterop> NewInteropMock()
        => new(new Mock<IJSRuntime>().Object, NullLogger<IndexedDbInterop>.Instance) { CallBase = false };

    private static IndexedDbTransaction NewTx(
        IndexedDbInterop interop,
        BlobShareRegistry registry,
        TxCompletionBridge bridge,
        TransactionMode mode = TransactionMode.ReadWrite)
    {
        var bridgeRef = DotNetObjectReference.Create(bridge);
        return new IndexedDbTransaction(
            interop,
            NullLoggerFactory.Instance,
            registry,
            txId: 7,
            mode: mode,
            storeNames: new[] { "things" },
            jsonOptions: new JsonSerializerOptions(),
            schema: new Dictionary<string, StoreSchema>(),
            bridge: bridge,
            bridgeRef: bridgeRef);
    }

    [TestMethod]
    public async Task CommitAsync_Success_FlipsIsActive_AndIsIdempotent()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var bridge = new TxCompletionBridge();
        var tx = NewTx(interop.Object, registry, bridge);

        Assert.IsTrue(tx.IsActive);
        var first = await tx.CommitAsync();
        Assert.IsTrue(first.IsSuccess);
        Assert.IsFalse(tx.IsActive);

        var second = await tx.CommitAsync();
        Assert.IsTrue(second.IsSuccess, "second commit should be a no-op success");
        interop.Verify(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task AbortAsync_Idempotent_AfterCommit()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync("commitTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var bridge = new TxCompletionBridge();
        var tx = NewTx(interop.Object, registry, bridge);

        await tx.CommitAsync();
        await tx.AbortAsync();

        interop.Verify(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task DisposeAsync_WhenStillActive_Aborts()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var bridge = new TxCompletionBridge();
        var tx = NewTx(interop.Object, registry, bridge);

        await tx.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
        Assert.IsFalse(tx.IsActive);
    }

    [TestMethod]
    public async Task DoubleDispose_DoesNotDoubleAbort()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var bridge = new TxCompletionBridge();
        var tx = NewTx(interop.Object, registry, bridge);

        await tx.DisposeAsync();
        await tx.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task Completed_FaultsWith_TransactionException_OnError()
    {
        var interop = NewInteropMock();
        interop.Setup(x => x.InvokeVoidAsync("abortTransaction", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var bridge = new TxCompletionBridge();
        var tx = NewTx(interop.Object, registry, bridge);

        bridge.OnError("QuotaExceeded", "ran out of disk");

        var ex = await Assert.ThrowsExactlyAsync<IndexedDbTransactionException>(
            async () => await tx.Completed);
        Assert.AreEqual(IndexedDbErrorKind.QuotaExceeded, ex.Error.Kind);

        await tx.DisposeAsync();
    }

    [TestMethod]
    public void ObjectStore_OutOfScope_Throws()
    {
        var interop = NewInteropMock();
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var bridge = new TxCompletionBridge();
        var tx = NewTx(interop.Object, registry, bridge);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => tx.JsonObjectStore("not-listed"));
    }
}
