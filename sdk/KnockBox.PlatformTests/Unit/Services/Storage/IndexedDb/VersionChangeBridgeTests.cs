using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class VersionChangeBridgeTests
{
    [TestMethod]
    public async Task OnVersionChange_WithoutAttachedDatabase_DoesNothing()
    {
        var bridge = new VersionChangeBridge(NullLoggerFactory.Instance);

        // _database is null until AttachDatabase fires — must not throw.
        await bridge.OnVersionChange();
    }
}
