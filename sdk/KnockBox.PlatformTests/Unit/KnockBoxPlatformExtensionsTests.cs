using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform;
using KnockBox.Platform.Games;
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
        builder.AddKnockBoxPlatform(o =>
        {
            o.PluginDiscovery = PluginDiscoveryMode.Explicit;
            o.AddGameModule<FakeModule>();
        });

        using var app = builder.Build();

        var engine = app.Services.GetKeyedService<AbstractGameEngine>(FakeModule.Route);
        Assert.IsNotNull(engine);
        Assert.IsInstanceOfType<FakeEngine>(engine);
    }

    [TestMethod]
    public void AddGameModule_DoesNotFlipPluginDiscoveryMode()
    {
        // Regression guard for the old silent-flip behavior. The caller is now
        // solely responsible for setting PluginDiscovery; AddGameModule only
        // appends to ExplicitModules.
        var options = new KnockBoxPlatformOptions();

        options.AddGameModule<FakeModule>();

        Assert.AreEqual(PluginDiscoveryMode.Directory, options.PluginDiscovery);
        Assert.AreEqual(1, options.ExplicitModules.Count);
    }

    [TestMethod]
    public void AddKnockBoxPlatform_RegistersDefaultGameAvailabilityService()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddKnockBoxPlatform(o =>
        {
            o.PluginDiscovery = PluginDiscoveryMode.Explicit;
        });

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

        builder.AddKnockBoxPlatform(o =>
        {
            o.PluginDiscovery = PluginDiscoveryMode.Explicit;
        });

        using var app = builder.Build();

        var resolved = app.Services.GetRequiredService<IGameAvailabilityService>();
        Assert.AreSame(stub, resolved);
    }

    [TestMethod]
    public void AddKnockBoxPlatform_ThrowsWhenDirectoryModeConflictsWithExplicitModules()
    {
        var builder = WebApplication.CreateBuilder();

        // AddGameModule<T> appends to ExplicitModules but no longer flips
        // PluginDiscovery. Leaving PluginDiscovery at its default (Directory)
        // while registering explicit modules is the footgun the guard catches.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddKnockBoxPlatform(o =>
            {
                o.AddGameModule<FakeModule>();
                // PluginDiscovery intentionally left at default (Directory).
            }));

        StringAssert.Contains(ex.Message, "Directory");
        StringAssert.Contains(ex.Message, "AddGameModule");
    }

    [TestMethod]
    public void AddKnockBoxPlatform_DefaultAvailabilityService_GetAll_ReturnsSameReference()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddKnockBoxPlatform(o =>
        {
            o.PluginDiscovery = PluginDiscoveryMode.Explicit;
        });
        using var app = builder.Build();

        var availability = app.Services.GetRequiredService<IGameAvailabilityService>();

        // GetAll is called from the home page's module enumeration; make sure
        // the default impl isn't allocating a fresh dictionary per call.
        Assert.AreSame(availability.GetAll(), availability.GetAll());
    }

    [TestMethod]
    public void AddKnockBoxPlatform_RegistersLogicSharedServices_BeforePluginRegisterServices()
    {
        // Contract guard: by the time a plugin's RegisterServices runs, the
        // platform's core logic services must already be visible in the
        // collection. Plugins may rely on those services (via the container
        // they receive later); if the order ever flips, the dependency
        // silently breaks at resolve time.
        var capturingModule = new ServiceCollectionSnapshotModule();

        var builder = WebApplication.CreateBuilder();
        builder.AddKnockBoxPlatform(o =>
        {
            o.PluginDiscovery = PluginDiscoveryMode.Explicit;
            o.AddExplicitModule(capturingModule);
        });

        using var _ = builder.Build();

        Assert.IsNotNull(capturingModule.Snapshot,
            "Expected the module's RegisterServices to run during AddKnockBoxPlatform.");
        Assert.IsTrue(capturingModule.SawLogicSharedServices,
            "IProfanityFilter / ILobbyCodeService / IRandomNumberService must be registered before plugins run.");
    }

    [TestMethod]
    public void AddKnockBoxPlatform_PluginRegisterServices_RunsAfterRepositoriesAndStateServices()
    {
        // Smoke assertion for the RegisterRepositories -> RegisterValidators ->
        // RegisterStateServices -> plugins ordering. We don't want to hardwire
        // specific internal service types here; we just assert that *some*
        // services were already in the collection before the plugin ran.
        var capturingModule = new ServiceCollectionSnapshotModule();

        var builder = WebApplication.CreateBuilder();
        var beforeKnockboxCount = builder.Services.Count;
        builder.AddKnockBoxPlatform(o =>
        {
            o.PluginDiscovery = PluginDiscoveryMode.Explicit;
            o.AddExplicitModule(capturingModule);
        });
        using var _ = builder.Build();

        Assert.IsNotNull(capturingModule.Snapshot);
        Assert.IsTrue(
            capturingModule.Snapshot!.Count > beforeKnockboxCount + 5,
            "Plugin's RegisterServices should have run after repos/validators/state/logic-shared were registered.");
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

    private sealed class ServiceCollectionSnapshotModule : IGameModule
    {
        public string Name => "Snapshot";
        public string Description => "Test module that captures the IServiceCollection state at RegisterServices time.";
        public string RouteIdentifier => "snapshot-test";

        public List<ServiceDescriptor>? Snapshot { get; private set; }
        public bool SawLogicSharedServices { get; private set; }

        public void RegisterServices(IServiceCollection services)
        {
            Snapshot = [.. services];
            SawLogicSharedServices =
                services.Any(d => d.ServiceType == typeof(KnockBox.Platform.Filtering.IProfanityFilter))
                && services.Any(d => d.ServiceType == typeof(KnockBox.Platform.Games.ILobbyCodeService))
                && services.Any(d => d.ServiceType == typeof(KnockBox.Core.Services.Logic.RandomGeneration.IRandomNumberService));

            // Also register our own engine so the pipeline completes normally.
            services.AddGameEngine<FakeEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => _ => { };
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
