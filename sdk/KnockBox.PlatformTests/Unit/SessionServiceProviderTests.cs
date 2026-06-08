using KnockBox.Core.Services.State.Shared;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Covers the eviction grace period on <see cref="SessionServiceProvider"/> via
/// <see cref="FakeTimeProvider"/>. The three paths the plan called out:
/// reconnect-before-timer (keeps the service), reconnect-after-timer (fresh instance),
/// and the race window in between.
/// </summary>
[TestClass]
public sealed class SessionServiceProviderTests
{
    private sealed class TrackedService : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private static (SessionServiceProvider provider, FakeTimeProvider time) NewProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedService>();
        var rootProvider = services.BuildServiceProvider();

        var time = new FakeTimeProvider();
        var provider = new SessionServiceProvider(
            rootProvider,
            NullLogger<SessionServiceProvider>.Instance,
            time);
        return (provider, time);
    }

    [TestMethod]
    public void GetService_ReconnectBeforeGracePeriod_ReturnsSameInstance()
    {
        var (provider, time) = NewProvider();
        var token = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<TrackedService>(token);
        Assert.IsTrue(first.TryGetSuccess(out var reg1));

        // Release the first lifecycle token — eviction timer starts.
        reg1.LifecycleToken.Dispose();

        // Advance less than the eviction delay, then reconnect.
        time.Advance(TimeSpan.FromSeconds(30));

        var second = provider.GetService<TrackedService>(token);
        Assert.IsTrue(second.TryGetSuccess(out var reg2));

        Assert.AreSame(reg1.Service, reg2.Service,
            "Reconnecting within the grace period must return the cached instance.");
        Assert.IsFalse(reg1.Service.IsDisposed);
    }

    [TestMethod]
    public async Task GetService_ReconnectAfterGracePeriod_ReturnsFreshInstance()
    {
        var (provider, time) = NewProvider();
        var token = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<TrackedService>(token);
        Assert.IsTrue(first.TryGetSuccess(out var reg1));

        reg1.LifecycleToken.Dispose();

        // Advance past the eviction delay. FakeTimeProvider drives the Task.Delay's
        // internal timer synchronously; the drain hook joins the eviction continuation
        // so disposal is observed deterministically (no wall-clock wait).
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.WaitForPendingEvictionsAsync();

        var second = provider.GetService<TrackedService>(token);
        Assert.IsTrue(second.TryGetSuccess(out var reg2));

        Assert.AreNotSame(reg1.Service, reg2.Service,
            "Reconnecting after the grace period must return a fresh instance.");
        Assert.IsTrue(reg1.Service.IsDisposed,
            "The evicted original must have been disposed.");
    }

    [TestMethod]
    public async Task GetService_ReconnectExactlyAtGracePeriod_KeepsCachedInstance()
    {
        // Advancing exactly to the deadline runs the eviction continuation; any
        // reconnect that lands AFTER that continuation observes a fresh instance.
        // The realistic "exactly-at" for the caller is to reconnect right before
        // the continuation runs. That must still hit the cached path — otherwise
        // a sub-millisecond glitch would destroy the user's session.
        var (provider, time) = NewProvider();
        var token = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<TrackedService>(token);
        Assert.IsTrue(first.TryGetSuccess(out var reg1));

        reg1.LifecycleToken.Dispose();

        // Reconnect at 59.999s — still inside the grace window. Must return cached.
        time.Advance(TimeSpan.FromMilliseconds(59_999));
        var second = provider.GetService<TrackedService>(token);
        Assert.IsTrue(second.TryGetSuccess(out var reg2));

        Assert.AreSame(reg1.Service, reg2.Service);
        Assert.IsFalse(reg1.Service.IsDisposed);

        // Now advance past the deadline — reconnect already happened, so eviction
        // timer was cancelled when ReferenceCount went back to 1. Draining joins the
        // (cancelled) continuation; the instance must stay alive.
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.WaitForPendingEvictionsAsync();
        Assert.IsFalse(reg1.Service.IsDisposed);
    }

    [TestMethod]
    public async Task GetService_SameToken_TwoHolders_NotEvictedUntilBothReleased()
    {
        // Identity unification means a tab's Server circuit and its hub connection
        // acquire the SAME SessionToken. Releasing one holder must NOT evict the
        // shared instance while the other still holds it — eviction is gated on
        // ReferenceCount<=0. This is the core safety property of the unification.
        var (provider, time) = NewProvider();
        var token = new SessionToken(Guid.NewGuid());

        var circuit = provider.GetService<TrackedService>(token);
        var hub = provider.GetService<TrackedService>(token);
        Assert.IsTrue(circuit.TryGetSuccess(out var circuitReg));
        Assert.IsTrue(hub.TryGetSuccess(out var hubReg));
        Assert.AreSame(circuitReg.Service, hubReg.Service, "Both holders share one cached instance.");

        // First holder leaves. Count drops to 1 — no eviction timer should fire.
        circuitReg.LifecycleToken.Dispose();
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.WaitForPendingEvictionsAsync();
        Assert.IsFalse(hubReg.Service.IsDisposed,
            "The session must survive while the second holder still references it.");

        // Second holder leaves. Now count hits 0 → eviction after the grace period.
        hubReg.LifecycleToken.Dispose();
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.WaitForPendingEvictionsAsync();
        Assert.IsTrue(hubReg.Service.IsDisposed,
            "Once both holders release, the session evicts as normal.");
    }

    [TestMethod]
    public void GetService_SameToken_ReleaseThenReacquireWithinGrace_KeepsInstance()
    {
        // Models the Server-page → WASM (or reload) handoff for one tab: the first
        // holder releases, and a second acquisition within the grace window adopts
        // the still-warm instance rather than building a fresh one.
        var (provider, time) = NewProvider();
        var token = new SessionToken(Guid.NewGuid());

        var first = provider.GetService<TrackedService>(token);
        Assert.IsTrue(first.TryGetSuccess(out var reg1));
        reg1.LifecycleToken.Dispose();

        time.Advance(TimeSpan.FromSeconds(20));

        var second = provider.GetService<TrackedService>(token);
        Assert.IsTrue(second.TryGetSuccess(out var reg2));

        Assert.AreSame(reg1.Service, reg2.Service);
        Assert.IsFalse(reg1.Service.IsDisposed);
    }
}
