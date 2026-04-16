using System;
using System.Threading.Tasks;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public class SessionServiceProviderTests : ISessionServiceProviderContractTests<SessionServiceProvider>
{
    private static readonly TimeSpan TestEvictionDelay = TimeSpan.FromMilliseconds(50);

    protected override SessionServiceProvider CreateProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        return new SessionServiceProvider(
            serviceProvider,
            NullLogger<SessionServiceProvider>.Instance)
        {
            EvictionDelay = TestEvictionDelay
        };
    }

    protected override async Task ForceDisposalTimerExpirationAsync()
    {
        // Wait for the eviction timer (Task.Delay) to complete.
        await Task.Delay(TestEvictionDelay + TimeSpan.FromMilliseconds(200));
    }
}