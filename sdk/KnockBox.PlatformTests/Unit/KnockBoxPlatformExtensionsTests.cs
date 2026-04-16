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

namespace KnockBox.PlatformTests.Unit;

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

    [TestMethod]
    public void AddKnockBoxPlatform_ThrowsWhenDirectoryModeConflictsWithExplicitModules()
    {
        var builder = WebApplication.CreateBuilder();

        // AddGameModule<T> flips PluginDiscovery to Explicit and adds the
        // module. Reassigning PluginDiscovery back to Directory afterwards is
        // exactly the footgun the guard catches.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddKnockBoxPlatform(o =>
            {
                o.AddGameModule<FakeModule>();
                o.PluginDiscovery = PluginDiscoveryMode.Directory;
            }));

        StringAssert.Contains(ex.Message, "Directory");
        StringAssert.Contains(ex.Message, "AddGameModule");
    }

    [TestMethod]
    public void AddKnockBoxPlatform_DefaultAvailabilityService_GetAll_ReturnsSameReference()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddKnockBoxPlatform(o => o.PluginDiscovery = PluginDiscoveryMode.Explicit);
        using var app = builder.Build();

        var availability = app.Services.GetRequiredService<IGameAvailabilityService>();

        // GetAll is called from the home page's module enumeration; make sure
        // the default impl isn't allocating a fresh dictionary per call.
        Assert.AreSame(availability.GetAll(), availability.GetAll());
    }

    // Note: the duplicate-plugin-folder guard in MapPluginStaticAssets is not
    // covered by an automated test because it can only trigger on a
    // case-sensitive filesystem (two sibling dirs "Foo" and "foo"). Windows
    // developer machines can't simulate that scenario. The guard itself is
    // straightforward (HashSet.Add returning false => throw) and is covered by
    // code inspection.

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
