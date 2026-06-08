using System;
using System.Threading.Tasks;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public class SessionServiceProviderTests : ISessionServiceProviderContractTests<SessionServiceProvider>
{
    private static readonly TimeSpan TestEvictionDelay = TimeSpan.FromMinutes(1);

    private readonly FakeTimeProvider _time = new();
    private SessionServiceProvider _provider = null!;

    protected override SessionServiceProvider CreateProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        _provider = new SessionServiceProvider(
            serviceProvider,
            NullLogger<SessionServiceProvider>.Instance,
            _time)
        {
            EvictionDelay = TestEvictionDelay
        };
        return _provider;
    }

    protected override async Task ForceDisposalTimerExpirationAsync()
    {
        // Drive the eviction grace period off a fake clock (no wall-clock wait), then
        // join the eviction continuation so disposal is deterministically observed.
        _time.Advance(TestEvictionDelay + TimeSpan.FromMilliseconds(1));
        await _provider.WaitForPendingEvictionsAsync();
    }
}
