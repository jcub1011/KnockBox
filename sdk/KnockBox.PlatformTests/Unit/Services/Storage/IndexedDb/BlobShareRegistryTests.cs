using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class BlobShareRegistryTests
{
    private static BlobShareRegistry CreateRegistry()
        => new(NullLogger<BlobShareRegistry>.Instance);

    private static BlobShareEntry MakeEntry(
        DateTimeOffset? absoluteExpiresAt = null,
        TimeSpan? slidingExpiry = null,
        long length = 4)
        => new()
        {
            Token = Guid.NewGuid(),
            ContentType = "application/octet-stream",
            Length = length,
            Fetcher = (offset, count, ct) => ValueTask.FromResult(new byte[count]),
            AbsoluteExpiresAt = absoluteExpiresAt,
            SlidingExpiry = slidingExpiry,
        };

    [TestMethod]
    public void Register_Then_TryGet_Returns_The_Entry()
    {
        using var registry = CreateRegistry();
        var entry = MakeEntry();
        registry.Register(entry);

        var found = registry.TryGetAndTouch(entry.Token);
        Assert.IsNotNull(found);
        Assert.AreSame(entry, found);
    }

    [TestMethod]
    public void Remove_DeletesTheEntry()
    {
        using var registry = CreateRegistry();
        var entry = MakeEntry();
        registry.Register(entry);
        registry.Remove(entry.Token);

        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public void TryGet_EvictsOnAbsoluteExpiry()
    {
        using var registry = CreateRegistry();
        var entry = MakeEntry(absoluteExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        registry.Register(entry);

        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
        // Second lookup confirms eviction was persisted.
        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public void TryGet_EvictsOnSlidingExpiry()
    {
        using var registry = CreateRegistry();
        var entry = MakeEntry(slidingExpiry: TimeSpan.FromMilliseconds(1));
        // Set lastAccessed into the past so the sliding window has already passed.
        entry.LastAccessedUtc = DateTimeOffset.UtcNow.AddSeconds(-10);
        registry.Register(entry);

        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public void TryGet_TouchesLastAccessed_OnSuccess()
    {
        using var registry = CreateRegistry();
        var entry = MakeEntry();
        entry.LastAccessedUtc = DateTimeOffset.UtcNow.AddDays(-1);
        registry.Register(entry);

        var before = entry.LastAccessedUtc;
        var found = registry.TryGetAndTouch(entry.Token);
        Assert.IsNotNull(found);
        Assert.IsGreaterThan(before, entry.LastAccessedUtc);
    }

    [TestMethod]
    public void UnknownToken_ReturnsNull()
    {
        using var registry = CreateRegistry();
        Assert.IsNull(registry.TryGetAndTouch(Guid.NewGuid()));
    }
}
