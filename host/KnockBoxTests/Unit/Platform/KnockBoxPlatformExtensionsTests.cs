using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform;
using KnockBox.Services.Logic.Games.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Tests.Unit.Platform;

[TestClass]
public sealed class KnockBoxPlatformExtensionsTests
{
    [TestMethod]
    public void AddKnockBoxPlatform_ExplicitMode_RegistersEngineKeyedByRouteIdentifier()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddKnockBoxPlatform(o => o.AddGameModule<FakeModule>());

        using var app = builder.Build();

        var engine = app.Services.GetKeyedService<AbstractGameEngine>(FakeModule.Route);
        Assert.IsNotNull(engine);
        Assert.IsInstanceOfType<FakeEngine>(engine);
    }

    [TestMethod]
    public void AddKnockBoxPlatform_RegistersDefaultGameAvailabilityService()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddKnockBoxPlatform(o => o.PluginDiscovery = PluginDiscoveryMode.Explicit);

        using var app = builder.Build();

        var availability = app.Services.GetRequiredService<IGameAvailabilityService>();
        Assert.IsTrue(availability.IsEnabled("any-route"));
    }

    [TestMethod]
    public void AddKnockBoxPlatform_HostOverrideWinsOverDefaultAvailabilityService()
    {
        var builder = WebApplication.CreateBuilder();
        var stub = new StubAvailabilityService();
        builder.Services.AddSingleton<IGameAvailabilityService>(stub);

        builder.AddKnockBoxPlatform(o => o.PluginDiscovery = PluginDiscoveryMode.Explicit);

        using var app = builder.Build();

        var resolved = app.Services.GetRequiredService<IGameAvailabilityService>();
        Assert.AreSame(stub, resolved);
    }

    private sealed class FakeModule : IGameModule
    {
        public const string Route = "fake-route";
        public string Name => "Fake";
        public string Description => "Fake test module.";
        public string RouteIdentifier => Route;

        public void RegisterServices(IServiceCollection services)
            => services.AddGameEngine<FakeEngine>(RouteIdentifier);

        public RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class FakeEngine : AbstractGameEngine
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
            => throw new NotImplementedException();

        public override Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class StubAvailabilityService : IGameAvailabilityService
    {
        public bool IsEnabled(string routeIdentifier) => false;
        public Task SetEnabledAsync(string routeIdentifier, bool enabled) => Task.CompletedTask;
        public IReadOnlyDictionary<string, bool> GetAll() => new Dictionary<string, bool>();
        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
