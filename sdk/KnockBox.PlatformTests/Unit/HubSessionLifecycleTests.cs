using KnockBox.Core.Services.State.Shared;
using KnockBox.Platform.Hubs;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// End-to-end coverage of the hub session lifecycle: the per-token acquire/release
/// in <see cref="GameConnectionRegistry"/> driving the real
/// <see cref="SessionServiceProvider"/> grace period, exercising the same eviction
/// behaviour the circuit path relies on but through the new hub caller.
/// </summary>
[TestClass]
public sealed class HubSessionLifecycleTests
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
        var time = new FakeTimeProvider();
        var provider = new SessionServiceProvider(
            services.BuildServiceProvider(), NullLogger<SessionServiceProvider>.Instance, time);
        return (provider, time);
    }

    /// <summary>
    /// Mints a lifecycle-token factory that acquires the session service via the
    /// provider, mirroring what <c>GameHub.OnConnectedAsync</c> does.
    /// </summary>
    private static Func<IDisposable> AcquireVia(SessionServiceProvider provider, SessionToken token, out Func<TrackedService?> currentService)
    {
        TrackedService? captured = null;
        currentService = () => captured;
        return () =>
        {
            var reg = provider.GetService<TrackedService>(token);
            Assert.IsTrue(reg.TryGetSuccess(out var registration));
            captured = registration.Service;
            return registration.LifecycleToken;
        };
    }

    [TestMethod]
    public void Connect_AcquiresSessionService()
    {
        var (provider, _) = NewProvider();
        var registry = new GameConnectionRegistry();
        var token = new SessionToken(Guid.NewGuid());

        registry.AddSession(token.Token, "conn-1", AcquireVia(provider, token, out var service));

        Assert.IsNotNull(service());
        Assert.IsFalse(service()!.IsDisposed);
    }

    [TestMethod]
    public void ReconnectWithinGrace_KeepsSameSessionService()
    {
        var (provider, time) = NewProvider();
        var registry = new GameConnectionRegistry();
        var token = new SessionToken(Guid.NewGuid());

        registry.AddSession(token.Token, "conn-1", AcquireVia(provider, token, out var firstService));
        var original = firstService();

        // Last connection drops → release → grace timer starts.
        Assert.IsTrue(registry.RemoveSession("conn-1", out var lifecycleToken));
        lifecycleToken!.Dispose();

        // Reconnect (new connection) inside the grace window re-acquires the cache.
        time.Advance(TimeSpan.FromSeconds(30));
        registry.AddSession(token.Token, "conn-2", AcquireVia(provider, token, out var secondService));

        Assert.AreSame(original, secondService());
        Assert.IsFalse(original!.IsDisposed);
    }

    [TestMethod]
    public async Task ReconnectAfterGrace_GetsFreshSessionService()
    {
        var (provider, time) = NewProvider();
        var registry = new GameConnectionRegistry();
        var token = new SessionToken(Guid.NewGuid());

        registry.AddSession(token.Token, "conn-1", AcquireVia(provider, token, out var firstService));
        var original = firstService();

        Assert.IsTrue(registry.RemoveSession("conn-1", out var lifecycleToken));
        lifecycleToken!.Dispose();

        // Past the grace window: the original is evicted + disposed.
        time.Advance(TimeSpan.FromMinutes(2));
        await provider.WaitForPendingEvictionsAsync();

        registry.AddSession(token.Token, "conn-2", AcquireVia(provider, token, out var secondService));

        Assert.AreNotSame(original, secondService());
        Assert.IsTrue(original!.IsDisposed);
    }
}
