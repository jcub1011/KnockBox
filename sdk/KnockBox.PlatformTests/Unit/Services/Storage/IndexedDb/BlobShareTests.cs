using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class BlobShareTests
{
    [TestMethod]
    public async Task DisposeAsync_RemovesEntryFromRegistry()
    {
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var token = Guid.NewGuid();
        registry.Register(new BlobShareEntry
        {
            Token = token,
            ContentType = "application/octet-stream",
            Length = 4,
            CircuitScopeId = Guid.NewGuid(),
            StreamOpener = _ => ValueTask.FromResult<Stream>(new MemoryStream(new byte[4], writable: false)),
        });
        var share = new BlobShare(registry, token, $"/blob-share/{token:D}", "application/octet-stream", 4);

        Assert.IsNotNull(registry.TryGetAndTouch(token));
        await share.DisposeAsync();
        Assert.IsNull(registry.TryGetAndTouch(token));
    }

    [TestMethod]
    public async Task DisposeAsync_InvokesOnDisposeCallback()
    {
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var token = Guid.NewGuid();
        registry.Register(new BlobShareEntry
        {
            Token = token,
            ContentType = "application/octet-stream",
            Length = 0,
            CircuitScopeId = Guid.NewGuid(),
            StreamOpener = _ => ValueTask.FromResult<Stream>(Stream.Null),
        });
        var called = false;
        var share = new BlobShare(registry, token, "/blob-share/x", "application/octet-stream", 0, onDispose: () => called = true);

        await share.DisposeAsync();
        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task DisposeAsync_IsIdempotent()
    {
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var token = Guid.NewGuid();
        registry.Register(new BlobShareEntry
        {
            Token = token,
            ContentType = "application/octet-stream",
            Length = 0,
            CircuitScopeId = Guid.NewGuid(),
            StreamOpener = _ => ValueTask.FromResult<Stream>(Stream.Null),
        });
        var callCount = 0;
        var share = new BlobShare(registry, token, "/blob-share/x", "application/octet-stream", 0, onDispose: () => callCount++);

        await share.DisposeAsync();
        await share.DisposeAsync();

        Assert.AreEqual(1, callCount);
    }

    [TestMethod]
    public void Properties_AreSurfacedFromCtor()
    {
        using var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var token = Guid.NewGuid();
        var share = new BlobShare(registry, token, "/blob-share/abc", "image/png", 999);

        Assert.AreEqual(token, share.Token);
        Assert.AreEqual("/blob-share/abc", share.Url);
        Assert.AreEqual("image/png", share.ContentType);
        Assert.AreEqual(999, share.Length);
    }
}
