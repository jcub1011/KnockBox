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
        long length = 4,
        Guid? circuitScopeId = null)
        => new()
        {
            Token = Guid.NewGuid(),
            ContentType = "application/octet-stream",
            Length = length,
            CircuitScopeId = circuitScopeId ?? Guid.NewGuid(),
            StreamOpener = _ => ValueTask.FromResult<Stream>(new MemoryStream(new byte[length], writable: false)),
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

    [TestMethod]
    public void ScopeGate_ReusedAcrossEntries_FromSameCircuit()
    {
        // All entries published by one circuit share one gate so the
        // endpoint can serialize JS-stream opens against that circuit.
        // Locking this in prevents a future refactor from accidentally
        // minting a fresh gate per entry, which would re-introduce the
        // unbounded parallel-stream fan-out that crashed the host.
        using var registry = CreateRegistry();
        var scope = Guid.NewGuid();
        var a = MakeEntry(circuitScopeId: scope);
        var b = MakeEntry(circuitScopeId: scope);
        registry.Register(a);
        registry.Register(b);

        var gateA = registry.TryGetScopeGate(scope);
        var gateB = registry.TryGetScopeGate(scope);
        Assert.IsNotNull(gateA);
        Assert.AreSame(gateA, gateB,
            "Same scope must yield the same semaphore so concurrent fetches actually serialize.");
    }

    [TestMethod]
    public void ScopeGate_DroppedAfterLastEntryRemoved()
    {
        // Refcount cleanup: each Register increments, each Remove decrements.
        // When the last entry for a scope is removed the gate is disposed
        // and the slot freed so long-running servers don't leak a semaphore
        // per ever-seen circuit. Re-registering under the same scope mints
        // a fresh gate.
        using var registry = CreateRegistry();
        var scope = Guid.NewGuid();
        var a = MakeEntry(circuitScopeId: scope);
        var b = MakeEntry(circuitScopeId: scope);
        registry.Register(a);
        registry.Register(b);

        var gateBefore = registry.TryGetScopeGate(scope);
        Assert.IsNotNull(gateBefore);

        registry.Remove(a.Token);
        Assert.IsNotNull(registry.TryGetScopeGate(scope),
            "Gate must survive while at least one entry for the scope remains.");

        registry.Remove(b.Token);
        Assert.IsNull(registry.TryGetScopeGate(scope),
            "Gate must be removed once the last entry for a scope is gone.");

        var c = MakeEntry(circuitScopeId: scope);
        registry.Register(c);
        var gateAfter = registry.TryGetScopeGate(scope);
        Assert.IsNotNull(gateAfter);
        Assert.AreNotSame(gateBefore, gateAfter,
            "Re-registering under the same scope must mint a fresh gate, not reuse the disposed one.");
    }
}
