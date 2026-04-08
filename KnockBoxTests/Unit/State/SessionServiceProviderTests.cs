using System;
using System.Threading.Tasks;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnockBox.Tests.Unit.State;

#pragma warning disable CS0618
public class TestClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
#pragma warning restore CS0618

[TestClass]
public class SessionServiceProviderTests : ISessionServiceProviderContractTests<SessionServiceProvider>
{
    private TestClock _clock = null!;
    private MemoryCache _cache = null!;

    [TestCleanup]
    public void Cleanup()
    {
        _cache?.Dispose();
    }

    protected override SessionServiceProvider CreateProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        _clock = new TestClock();
        
        var options = new OptionsWrapper<MemoryCacheOptions>(new MemoryCacheOptions
        {
            Clock = _clock,
            ExpirationScanFrequency = TimeSpan.FromMilliseconds(10)
        });

        _cache = new MemoryCache(options);

        return new SessionServiceProvider(
            serviceProvider,
            _cache,
            NullLogger<SessionServiceProvider>.Instance);
    }

    protected override async Task ForceDisposalTimerExpirationAsync()
    {
        _clock.UtcNow += TimeSpan.FromMinutes(2);
        
        // Wait for the background scanner to pick up the expired items.
        // Also touch the cache to explicitly trigger a scan in some .NET versions.
        _cache.TryGetValue(new object(), out _);
        await Task.Delay(100);
        _cache.TryGetValue(new object(), out _);
    }
}