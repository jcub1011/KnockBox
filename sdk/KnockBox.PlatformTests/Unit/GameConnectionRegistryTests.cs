using KnockBox.Platform.Hubs;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for the per-session-token lifecycle tracking in
/// <see cref="GameConnectionRegistry"/>: acquire-once on first connection,
/// release-once on last, per-tab independence, and same-tab reconnect tolerance.
/// </summary>
[TestClass]
public sealed class GameConnectionRegistryTests
{
    /// <summary>A disposable that records whether it was disposed.</summary>
    private sealed class TrackingToken : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [TestMethod]
    public void AddSession_FirstConnection_AcquiresExactlyOnce()
    {
        var registry = new GameConnectionRegistry();
        var acquireCount = 0;

        registry.AddSession("token-a", "conn-1", () => { acquireCount++; return new TrackingToken(); });
        registry.AddSession("token-a", "conn-2", () => { acquireCount++; return new TrackingToken(); });

        Assert.AreEqual(1, acquireCount);
        Assert.AreEqual(2, registry.CountForSession("token-a"));
    }

    [TestMethod]
    public void RemoveSession_ReleasesOnlyWhenLastConnectionLeaves()
    {
        var registry = new GameConnectionRegistry();
        var token = new TrackingToken();
        registry.AddSession("token-a", "conn-1", () => token);
        registry.AddSession("token-a", "conn-2", () => token);

        // First connection leaves: same tab still has another connection — no release.
        Assert.IsFalse(registry.RemoveSession("conn-1", out var first));
        Assert.IsNull(first);

        // Last connection leaves: release the stashed lifecycle token.
        Assert.IsTrue(registry.RemoveSession("conn-2", out var last));
        Assert.AreSame(token, last);
        Assert.AreEqual(0, registry.CountForSession("token-a"));
    }

    [TestMethod]
    public void AddSession_DistinctTokens_AreIndependentSessions()
    {
        var registry = new GameConnectionRegistry();
        var acquireCount = 0;

        registry.AddSession("token-a", "conn-1", () => { acquireCount++; return new TrackingToken(); });
        registry.AddSession("token-b", "conn-2", () => { acquireCount++; return new TrackingToken(); });

        // Two tabs → two distinct sessions, each acquired once.
        Assert.AreEqual(2, acquireCount);
        Assert.IsTrue(registry.RemoveSession("conn-1", out _));
        Assert.AreEqual(1, registry.CountForSession("token-b"));
    }

    [TestMethod]
    public void RemoveSession_UnknownConnection_ReturnsFalse()
    {
        var registry = new GameConnectionRegistry();

        Assert.IsFalse(registry.RemoveSession("never-added", out var token));
        Assert.IsNull(token);
    }

    [TestMethod]
    public void AddSession_ReacquireAfterLastLeft_AcquiresAgain()
    {
        var registry = new GameConnectionRegistry();
        var acquireCount = 0;

        registry.AddSession("token-a", "conn-1", () => { acquireCount++; return new TrackingToken(); });
        registry.RemoveSession("conn-1", out _);
        registry.AddSession("token-a", "conn-2", () => { acquireCount++; return new TrackingToken(); });

        Assert.AreEqual(2, acquireCount);
    }
}
