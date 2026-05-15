using System.Text.Json;
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
            bridgeRef: bridgeRef);

    private static (VersionChangeBridge bridge, DotNetObjectReference<VersionChangeBridge> bridgeRef) NewBridge()
    {
        var bridge = new VersionChangeBridge(NullLoggerFactory.Instance);
        return (bridge, DotNetObjectReference.Create(bridge));
    }

    [TestMethod]
    public async Task DisposeAsync_HappyPath_ClosesAndDisposesBridge()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge();
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
        var (_, bridgeRef) = NewBridge();
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
        var (_, bridgeRef) = NewBridge();
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
        var (_, bridgeRef) = NewBridge();
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
        var (_, bridgeRef) = NewBridge();
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
        var (_, bridgeRef) = NewBridge();
        var db = NewDb(interop.Object, registry, bridgeRef);

        await db.RaiseVersionChangeRequestedAsync();
        await db.DisposeAsync();
    }
}
