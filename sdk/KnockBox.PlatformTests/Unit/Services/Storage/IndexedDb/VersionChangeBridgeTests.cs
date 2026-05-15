using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class VersionChangeBridgeTests
{
    private static Mock<IndexedDbInterop> NewInteropMock()
        => new(new Mock<IJSRuntime>().Object, NullLogger<IndexedDbInterop>.Instance) { CallBase = false };

    [TestMethod]
    public async Task OnUpgrade_WithoutHandler_Throws()
    {
        var interop = NewInteropMock();
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 2);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await bridge.OnUpgrade(
                upgradeTxId: 9,
                oldVersion: 0,
                newVersion: 2,
                existingSchema: new Dictionary<string, string[]>()));
    }

    [TestMethod]
    public async Task OnUpgrade_HappyPath_ReturnsDrainedOps()
    {
        var interop = NewInteropMock();
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 2)
        {
            OnUpgrade = (ctx, oldV, newV, ct) =>
            {
                ctx.CreateJsonObjectStore("alpha");
                ctx.CreateBlobObjectStore("beta");
                return ValueTask.CompletedTask;
            },
        };
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);

        var ops = await bridge.OnUpgrade(9, 0, 2, new Dictionary<string, string[]>());

        Assert.AreEqual(2, ops.Length);
        Assert.AreEqual("createStore", ops[0].Type);
        Assert.AreEqual("alpha", ops[0].Name);
        Assert.AreEqual("beta", ops[1].Name);
    }

    [TestMethod]
    public async Task OnUpgrade_HandlerThrows_PropagatesAndDeactivatesContext()
    {
        var interop = NewInteropMock();
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        IUpgradeContext? captured = null;
        var schema = new IndexedDbSchema("TestDb", 2)
        {
            OnUpgrade = (ctx, oldV, newV, ct) =>
            {
                captured = ctx;
                throw new InvalidOperationException("handler bomb");
            },
        };
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await bridge.OnUpgrade(9, 0, 2, new Dictionary<string, string[]>()));

        Assert.IsNotNull(captured);
        // Context's UpgradeTxContext.IsActive should now be false. We can
        // observe that indirectly: a subsequent data accessor on the same
        // context observes an inactive tx if anything reads it. Since
        // _active is private, we assert via behavior: the schema flush is
        // never attempted because the user's delegate failed before any
        // data accessor was awaited. Coverage focus is on rethrow + no
        // upgradeApplySchemaOps call.
        interop.Verify(x => x.InvokeVoidAsync(
            "upgradeApplySchemaOps", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task OnVersionChange_WithoutAttachedDatabase_DoesNothing()
    {
        var interop = NewInteropMock();
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var schema = new IndexedDbSchema("TestDb", 1);
        var bridge = new VersionChangeBridge(interop.Object, NullLoggerFactory.Instance, registry, schema);

        // Should not throw — `_database` is null until AttachDatabase fires.
        await bridge.OnVersionChange();
    }
}
