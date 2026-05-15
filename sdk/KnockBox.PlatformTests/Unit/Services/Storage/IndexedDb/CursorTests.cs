using System.Text.Json;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class CursorTests
{
    private static string EntryJson(string keyJson, string valueJson)
        => $"{{\"key\":{keyJson},\"primaryKey\":{keyJson},\"value\":{valueJson}}}";

    public sealed record User(string Name);

    // ────────── IndexedDbCursor<TValue> (typed) ──────────

    [TestMethod]
    public async Task TypedCursor_BufferedFirst_YieldsThenContinuesUntilDone()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var entry = JsonSerializer.Deserialize<JsonElement>(
            EntryJson(IndexedDbTestHelpers.NumberKeyJson(1), "{\"name\":\"a\"}"));
        var firstEntry = IndexedDbCursor<User>.ParseEntry(entry, IndexedDbWireFormat.DefaultJsonOptions);

        var continueResponses = new Queue<CursorMoveResponse>(new[]
        {
            new CursorMoveResponse(false, JsonSerializer.Deserialize<JsonElement>(
                EntryJson(IndexedDbTestHelpers.NumberKeyJson(2), "{\"name\":\"b\"}"))),
            new CursorMoveResponse(true, null),
        });
        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(() => new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>.FromValue(continueResponses.Dequeue())));
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, cursorId: 1, firstEntry);

        var names = new List<string>();
        while (await cursor.MoveNextAsync())
        {
            names.Add(cursor.Current!.Value.Value.Name);
        }
        await cursor.DisposeAsync();

        CollectionAssert.AreEqual(new[] { "a", "b" }, names);
        // releaseHandle called once after dispose.
        interop.Verify(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task TypedCursor_AsyncEnumerator_IteratesViaForeach()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var entry = JsonSerializer.Deserialize<JsonElement>(
            EntryJson(IndexedDbTestHelpers.NumberKeyJson(1), "{\"name\":\"a\"}"));
        var firstEntry = IndexedDbCursor<User>.ParseEntry(entry, IndexedDbWireFormat.DefaultJsonOptions);

        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>.FromValue(new CursorMoveResponse(true, null))));
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 1, firstEntry);

        var count = 0;
        await foreach (var item in cursor)
        {
            count++;
        }
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task TypedCursor_DisposeAsync_ReleasesHandle_Idempotent()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 5, firstEntry: null);

        await cursor.DisposeAsync();
        await cursor.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task TypedCursor_DisposedCursor_AllOpsShortCircuit()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 5, firstEntry: null);
        await cursor.DisposeAsync();

        Assert.IsFalse(await cursor.MoveNextAsync());
        var adv = await cursor.AdvanceAsync(1);
        var con = await cursor.ContinueAsync();
        Assert.IsTrue(adv.TryGetFailure(out var advErr) && advErr.Kind == IndexedDbErrorKind.TransactionInactive);
        Assert.IsTrue(con.TryGetFailure(out var conErr) && conErr.Kind == IndexedDbErrorKind.TransactionInactive);
    }

    [TestMethod]
    public async Task TypedCursor_DisposeAfterTxInactive_DoesNotCallRelease()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var cursor = new IndexedDbCursor<User>(ctx, 5, firstEntry: null);

        await cursor.DisposeAsync();

        interop.Verify(x => x.InvokeVoidAsync("releaseHandle", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task TypedCursor_ContinueAsync_Error_Propagates()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "gone")));

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 1, firstEntry: null);

        var result = await cursor.ContinueAsync();
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, err.Kind);
    }

    [TestMethod]
    public async Task TypedCursor_AdvanceAsync_ZeroCount_ReturnsDataError()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 1, firstEntry: null);

        var result = await cursor.AdvanceAsync(0);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
        interop.Verify(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorAdvance", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task TypedCursor_UpdateAsync_RoutesThroughInterop()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("cursorUpdate");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 1, firstEntry: null);

        var result = await cursor.UpdateAsync(new User("renamed"));
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task TypedCursor_DeleteAsync_RoutesThroughInterop()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("cursorDelete");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbCursor<User>(ctx, 1, firstEntry: null);

        var result = await cursor.DeleteAsync();
        Assert.IsTrue(result.IsSuccess);
    }

    // ────────── IndexedDbKeyCursor ──────────

    [TestMethod]
    public async Task KeyCursor_BufferedFirst_Then_Done()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var keyEntry = JsonSerializer.Deserialize<JsonElement>(
            $"{{\"key\":{IndexedDbTestHelpers.NumberKeyJson(7)},\"primaryKey\":{IndexedDbTestHelpers.NumberKeyJson(7)}}}");
        var first = IndexedDbKeyCursor.ParseEntry(keyEntry);
        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>.FromValue(new CursorMoveResponse(true, null))));
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbKeyCursor(ctx, 1, first);

        Assert.IsTrue(await cursor.MoveNextAsync());
        Assert.AreEqual(7.0, (double)cursor.Current!.Value.Key.Value!);
        Assert.IsFalse(await cursor.MoveNextAsync());
        await cursor.DisposeAsync();
    }

    [TestMethod]
    public async Task KeyCursor_AdvanceAsync_Failure_Propagates()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorAdvance", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.Unknown, "oops")));

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new IndexedDbKeyCursor(ctx, 1, firstEntry: null);

        var result = await cursor.AdvanceAsync(2);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Unknown, err.Kind);
    }

    // ────────── JsonObjectCursor ──────────

    [TestMethod]
    public async Task JsonCursor_BufferedFirst_Iterates_AndUpdateRoutes()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var entry = JsonSerializer.Deserialize<JsonElement>(
            EntryJson(IndexedDbTestHelpers.NumberKeyJson(1), "{\"v\":42}"));
        var first = JsonObjectCursor.ParseEntry(entry);
        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>.FromValue(new CursorMoveResponse(true, null))));
        interop.SetupVoidSuccess("cursorUpdate");
        interop.SetupVoidSuccess("cursorDelete");
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new JsonObjectCursor(ctx, 1, first);

        Assert.IsTrue(await cursor.MoveNextAsync());
        var u = await cursor.UpdateAsync(JsonSerializer.Deserialize<JsonElement>("{\"v\":99}"));
        Assert.IsTrue(u.IsSuccess);
        var d = await cursor.DeleteAsync();
        Assert.IsTrue(d.IsSuccess);
        await cursor.DisposeAsync();
    }

    // ────────── BlobObjectCursor ──────────

    [TestMethod]
    public async Task BlobCursor_BufferedFirst_ParsesBlob()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        var entryJson = $"{{\"key\":{IndexedDbTestHelpers.NumberKeyJson(1)},\"primaryKey\":{IndexedDbTestHelpers.NumberKeyJson(1)},\"value\":{{\"blobId\":5,\"contentType\":\"image/png\",\"length\":1024}}}}";
        var entry = JsonSerializer.Deserialize<JsonElement>(entryJson);

        using var registry = IndexedDbTestHelpers.NewRegistry();
        var first = BlobObjectCursor.ParseEntry(interop.Object, NullLoggerFactory.Instance, registry, entry);

        interop.Setup(x => x.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>>(
                KnockBox.Core.Primitives.Returns.ValueResult<CursorMoveResponse, IndexedDbError>.FromValue(new CursorMoveResponse(true, null))));
        interop.SetupVoidSuccess("releaseHandle");

        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new BlobObjectCursor(ctx, NullLoggerFactory.Instance, registry, 1, first);

        Assert.IsTrue(await cursor.MoveNextAsync());
        Assert.AreEqual(1024, cursor.Current!.Value.Value.Length);
        await cursor.DisposeAsync();
    }

    [TestMethod]
    public async Task BlobCursor_UpdateAsync_RejectsForeignBlob()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new BlobObjectCursor(ctx, NullLoggerFactory.Instance, registry, 1, firstEntry: null);

        var result = await cursor.UpdateAsync(new BlobObjectStoreTestsForeignBlob());
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task BlobCursor_UpdateAsync_InactiveTransaction_ShortCircuits()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object) { IsActive = false };
        var cursor = new BlobObjectCursor(ctx, NullLoggerFactory.Instance, registry, 1, firstEntry: null);
        var blob = new IndexedDbBlobImpl(interop.Object, NullLogger<IndexedDbBlobImpl>.Instance, registry, 7, "image/png", 8);

        var result = await cursor.UpdateAsync(blob);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.TransactionInactive, err.Kind);
    }

    [TestMethod]
    public async Task BlobCursor_UpdateAsync_HappyPath_RoutesToCursorUpdateBlob()
    {
        var interop = IndexedDbTestHelpers.NewInteropMock();
        interop.SetupVoidSuccess("cursorUpdateBlob");
        using var registry = IndexedDbTestHelpers.NewRegistry();
        var ctx = new IndexedDbTestHelpers.TestTxContext(interop.Object);
        var cursor = new BlobObjectCursor(ctx, NullLoggerFactory.Instance, registry, 1, firstEntry: null);
        var blob = new IndexedDbBlobImpl(interop.Object, NullLogger<IndexedDbBlobImpl>.Instance, registry, 7, "image/png", 8);

        var result = await cursor.UpdateAsync(blob);
        Assert.IsTrue(result.IsSuccess);
    }

    // Foreign blob fixture for cross-test reuse.
    public sealed class BlobObjectStoreTestsForeignBlob : IndexedDbBlob
    {
        public override string ContentType => "x/y";
        public override long Length => 0;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public override ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => ValueTask.FromResult(Array.Empty<byte>());
        public override ValueTask<Stream> OpenReadAsync(CancellationToken ct = default) => ValueTask.FromResult<Stream>(Stream.Null);
        public override ValueTask<string> CreateObjectUrlAsync(CancellationToken ct = default) => ValueTask.FromResult("");
        public override ValueTask<IBlobShare> PublishForSharingAsync(BlobShareOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
