using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class UpgradeContextTests
{
    private static Mock<IndexedDbInterop> NewInteropMock()
        => new(new Mock<IJSRuntime>().Object, NullLogger<IndexedDbInterop>.Instance) { CallBase = false };

    private static UpgradeContext NewCtx(IndexedDbInterop interop, IReadOnlyDictionary<string, IReadOnlyList<string>>? existing = null)
    {
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        return new UpgradeContext(
            interop,
            NullLoggerFactory.Instance,
            registry,
            upgradeTxId: 1,
            oldVersion: 0,
            newVersion: 1,
            jsonOptions: new JsonSerializerOptions(),
            existingSchema: existing ?? new Dictionary<string, IReadOnlyList<string>>());
    }

    [TestMethod]
    public async Task ObjectStoreAsync_AwaitsSchemaFlush_BeforeReturning()
    {
        var interop = NewInteropMock();
        var flushCompleted = false;
        var flushBarrier = new TaskCompletionSource();

        interop
            .Setup(x => x.InvokeVoidAsync(
                "upgradeApplySchemaOps", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(async () =>
            {
                await flushBarrier.Task;
                flushCompleted = true;
                return Result<IndexedDbError>.Success;
            });

        var ctx = NewCtx(interop.Object);
        ctx.CreateJsonObjectStore("things");

        var storeTask = ctx.JsonObjectStoreAsync("things").AsTask();
        Assert.IsFalse(storeTask.IsCompleted,
            "store handle must not be returned before the schema flush resolves");
        Assert.IsFalse(flushCompleted);

        flushBarrier.SetResult();
        var store = await storeTask;

        Assert.IsTrue(flushCompleted);
        Assert.IsNotNull(store);
        interop.Verify(x => x.InvokeVoidAsync(
            "upgradeApplySchemaOps", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [TestMethod]
    public async Task SchemaFlushFailure_SurfacedAsInvalidOperationException()
    {
        var interop = NewInteropMock();
        interop
            .Setup(x => x.InvokeVoidAsync(
                "upgradeApplySchemaOps", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(
                new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "tx gone")));

        var ctx = NewCtx(interop.Object);
        ctx.CreateJsonObjectStore("things");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await ctx.JsonObjectStoreAsync("things"));

        StringAssert.Contains(ex.Message, "TransactionInactive");
    }

    [TestMethod]
    public async Task EmptyPendingOps_SkipsFlush()
    {
        var existing = new Dictionary<string, IReadOnlyList<string>>
        {
            ["existing"] = Array.Empty<string>(),
        };
        var interop = NewInteropMock();

        var ctx = NewCtx(interop.Object, existing);
        var store = await ctx.JsonObjectStoreAsync("existing");

        Assert.IsNotNull(store);
        interop.Verify(x => x.InvokeVoidAsync(
            "upgradeApplySchemaOps", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public void CreateJsonObjectStore_DuplicateName_Throws()
    {
        var existing = new Dictionary<string, IReadOnlyList<string>>
        {
            ["dup"] = Array.Empty<string>(),
        };
        var interop = NewInteropMock();
        var ctx = NewCtx(interop.Object, existing);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => ctx.CreateJsonObjectStore("dup"));
    }

    [TestMethod]
    public void DeleteObjectStore_UnknownName_Throws()
    {
        var interop = NewInteropMock();
        var ctx = NewCtx(interop.Object);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => ctx.DeleteObjectStore("nope"));
    }

    [TestMethod]
    public async Task DataAccessor_ForUnknownStore_Throws()
    {
        var interop = NewInteropMock();
        var ctx = NewCtx(interop.Object);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await ctx.JsonObjectStoreAsync("missing"));
    }
}
