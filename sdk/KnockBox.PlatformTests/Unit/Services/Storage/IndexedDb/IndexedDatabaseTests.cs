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

    // ── JsonPutBatchAsync ────────────────────────────────────────────────

    [TestMethod]
    public async Task JsonPutBatchAsync_EmptyItems_ReturnsEmpty_DoesNotInvokeInterop()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge();
        var db = NewDb(interop.Object, registry, bridgeRef);

        var result = await db.JsonPutBatchAsync(Array.Empty<JsonPutItem>());

        Assert.IsTrue(result.TryGetSuccess(out var keys));
        Assert.AreEqual(0, keys.Count);
        // Empty batch should short-circuit — no JS round-trip at all.
        interop.Verify(x => x.InvokeRawAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task JsonPutBatchAsync_SingleStore_PassesEnvelopesAndParsesKeys()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        // JS would return a JSON array of key envelopes (one per input).
        var resultJson = $"[{IndexedDbTestHelpers.StringKeyJson("k1")},{IndexedDbTestHelpers.StringKeyJson("k2")}]";
        interop.SetupRawSuccess("batchOpJsonPut", resultJson);
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge();
        var db = NewDb(interop.Object, registry, bridgeRef, dbId: 42, storeNames: "things");

        var items = new[]
        {
            new JsonPutItem("things", new { name = "alpha" }, IndexedDbKey.String("k1")),
            new JsonPutItem("things", new { name = "beta" },  IndexedDbKey.String("k2")),
        };
        var result = await db.JsonPutBatchAsync(items);

        Assert.IsTrue(result.TryGetSuccess(out var keys));
        Assert.AreEqual(2, keys.Count);
        Assert.AreEqual(IndexedDbKeyKind.String, keys[0].Kind);
        Assert.AreEqual("k1", keys[0].Value);
        Assert.AreEqual("k2", keys[1].Value);

        // Verify the call shape: method name, dbId, and that the JS-side
        // payload is an object array carrying our two items. No per-item
        // unwrap here — the envelope contract is covered by
        // IndexedDbWireFormat tests; we just confirm the right number of
        // entries reached the interop.
        interop.Verify(x => x.InvokeRawAsync(
            "batchOpJsonPut",
            It.IsAny<CancellationToken>(),
            It.Is<object?[]>(args => BatchPayloadOf(args, expectedDbId: 42, expectedCount: 2))),
            Times.Once);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task JsonPutBatchAsync_MultiStore_RoutesEachItemToOwnStoreInPayload()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var resultJson = $"[{IndexedDbTestHelpers.StringKeyJson("a")},{IndexedDbTestHelpers.StringKeyJson("b")},{IndexedDbTestHelpers.StringKeyJson("c")}]";
        interop.SetupRawSuccess("batchOpJsonPut", resultJson);
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge();
        var db = NewDb(interop.Object, registry, bridgeRef, storeNames: new[] { "storeA", "storeB" });

        var items = new[]
        {
            new JsonPutItem("storeA", new { v = 1 }, IndexedDbKey.String("a")),
            new JsonPutItem("storeB", new { v = 2 }, IndexedDbKey.String("b")),
            new JsonPutItem("storeA", new { v = 3 }, IndexedDbKey.String("c")),
        };
        var result = await db.JsonPutBatchAsync(items);

        Assert.IsTrue(result.TryGetSuccess(out var keys));
        Assert.AreEqual(3, keys.Count);

        // The JS-side envelope is opaque from this layer's perspective, but
        // we can verify the payload array carries one entry per input — the
        // JS module is what walks i.storeName to decide which store each
        // record lands in.
        interop.Verify(x => x.InvokeRawAsync(
            "batchOpJsonPut",
            It.IsAny<CancellationToken>(),
            It.Is<object?[]>(args => BatchPayloadOf(args, expectedDbId: 1, expectedCount: 3))),
            Times.Once);
        await db.DisposeAsync();
    }

    // Helper for verifying the batchOpJsonPut interop shape. Lifted out of
    // the It.Is predicate body because Moq compiles those into expression
    // trees, which don't allow pattern matching.
    private static bool BatchPayloadOf(object?[] args, int expectedDbId, int expectedCount)
    {
        if (args.Length != 2) return false;
        if (args[0] is not int dbId || dbId != expectedDbId) return false;
        if (args[1] is not object?[] payload) return false;
        return payload.Length == expectedCount;
    }

    [TestMethod]
    public async Task JsonPutBatchAsync_InteropFailure_SurfacesError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupRawFailure("batchOpJsonPut",
            new IndexedDbError(IndexedDbErrorKind.Aborted, "tx aborted"));
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge();
        var db = NewDb(interop.Object, registry, bridgeRef);

        var items = new[]
        {
            new JsonPutItem("things", new { x = 1 }, IndexedDbKey.String("k")),
        };
        var result = await db.JsonPutBatchAsync(items);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Aborted, err.Kind);
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task JsonPutBatchAsync_NonArrayResponse_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        // JS returned an object instead of an array — protocol mismatch.
        interop.SetupRawSuccess("batchOpJsonPut", "{\"unexpected\":true}");
        interop.SetupVoidSuccess("closeDatabase");

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var (_, bridgeRef) = NewBridge();
        var db = NewDb(interop.Object, registry, bridgeRef);

        var items = new[]
        {
            new JsonPutItem("things", new { x = 1 }, IndexedDbKey.String("k")),
        };
        var result = await db.JsonPutBatchAsync(items);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
        await db.DisposeAsync();
    }
}
