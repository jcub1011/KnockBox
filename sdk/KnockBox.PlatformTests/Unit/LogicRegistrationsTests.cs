using System.Runtime.Loader;
using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Storage;
using KnockBox.Services.Registrations.Logic;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for <see cref="LogicRegistrations.RegisterLogic"/>. Under the soft
/// sandbox the plugin sees only <see cref="IPluginRegistration"/> (never the
/// raw <see cref="IServiceCollection"/>), so the malicious-key/duplicate-key
/// attacks the previous safety net guarded against are no longer expressible.
/// What's left is the shape of the call itself: exactly-one
/// <c>AddGameEngine</c> per plugin, failure containment across plugins, and
/// the plugin context wiring that replaces direct service-collection access.
/// </summary>
[TestClass]
public sealed class LogicRegistrationsTests
{
    [TestMethod]
    public void Preserves_LegitimateRegistrations_ViaHelper()
    {
        var services = CreateBaselineServices();
        var logger = NullLogger.Instance;

        var module = new GoodModule("good");
        services.RegisterLogic(new PluginLoadResult([ToLoadedPlugin(module)], []), logger);

        var provider = services.BuildServiceProvider();

        var keyed = provider.GetKeyedService<AbstractGameEngine>("good");
        Assert.IsNotNull(keyed, "The legitimate keyed engine must survive RegisterLogic.");
        Assert.IsInstanceOfType<TestEngine>(keyed);

        var concrete = provider.GetService<TestEngine>();
        Assert.IsNotNull(concrete, "The concrete singleton registered by AddGameEngine must also survive.");
        Assert.AreSame(keyed, concrete, "Keyed and concrete registrations must resolve to the same instance.");
    }

    [TestMethod]
    public void ThrowingPlugin_DoesNotBlock_LaterPluginWithSameRoute()
    {
        var services = CreateBaselineServices();
        var logger = new CapturingLogger();

        var throwing = new ThrowingModule("shared-route");
        var good = new GoodModule("shared-route");

        services.RegisterLogic(
            new PluginLoadResult([ToLoadedPlugin(throwing), ToLoadedPlugin(good)], []),
            logger);

        var provider = services.BuildServiceProvider();

        var keyed = provider.GetKeyedService<AbstractGameEngine>("shared-route");
        Assert.IsNotNull(keyed,
            "A throwing plugin must not prevent the next plugin from registering its engine.");
        Assert.IsInstanceOfType<TestEngine>(keyed);
    }

    [TestMethod]
    public void Plugin_CallingAddGameEngine_MoreThanOnce_LogsError()
    {
        var services = CreateBaselineServices();
        var logger = new CapturingLogger();

        var module = new DoubleEngineModule("twice");
        services.RegisterLogic(new PluginLoadResult([ToLoadedPlugin(module)], []), logger);

        Assert.IsTrue(
            logger.Errors.Any(e => e.Contains("AddGameEngine", StringComparison.Ordinal)
                                 && e.Contains("2", StringComparison.Ordinal)),
            "An Error must be logged explaining that AddGameEngine was called more than once.");
    }

    [TestMethod]
    public void Plugin_NeverCallingAddGameEngine_LogsErrorAndStillExposesModule()
    {
        var services = CreateBaselineServices();
        var logger = new CapturingLogger();

        var module = new NoEngineModule("empty");
        services.RegisterLogic(new PluginLoadResult([ToLoadedPlugin(module)], []), logger);

        Assert.IsTrue(
            logger.Errors.Any(e => e.Contains("AddGameEngine", StringComparison.Ordinal)
                                 && e.Contains("0", StringComparison.Ordinal)),
            "An Error must be logged explaining that AddGameEngine was never called.");

        var provider = services.BuildServiceProvider();
        var modules = provider.GetServices<IGameModule>().ToArray();
        Assert.Contains(module, modules,
            "Module must remain visible as IGameModule even when its engine registration was malformed, so the home page still lists it.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal DI baseline needed by the default <see cref="IPluginContext"/>
    /// factory inside <see cref="LogicRegistrations.RegisterLogic"/>.
    /// </summary>
    private static IServiceCollection CreateBaselineServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IStoragePathService>(new TempStoragePathService());
        return services;
    }

    private static LoadedPlugin ToLoadedPlugin(IGameModule module) =>
        new(
            Module: module,
            Manifest: module.Manifest,
            Assembly: module.GetType().Assembly,
            LoadContext: AssemblyLoadContext.GetLoadContext(module.GetType().Assembly) ?? AssemblyLoadContext.Default);

    private static IPluginManifest MakeManifest(string route) => new PluginManifest(
        Name: $"Module-{route}",
        Description: "test",
        RouteIdentifier: route,
        Version: new Version(1, 0, 0),
        EntryAssembly: "KnockBox.PlatformTests",
        Capabilities: new HashSet<PluginCapability>());

    // ── Fake modules & engines ───────────────────────────────────────────

    private sealed class GoodModule(string route) : IGameModule
    {
        public IPluginManifest Manifest { get; } = MakeManifest(route);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<TestEngine>();

        public RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class ThrowingModule(string route) : IGameModule
    {
        public IPluginManifest Manifest { get; } = MakeManifest(route);

        public void RegisterServices(IPluginRegistration registration)
            => throw new InvalidOperationException("intentional");

        public RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class DoubleEngineModule(string route) : IGameModule
    {
        public IPluginManifest Manifest { get; } = MakeManifest(route);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddGameEngine<TestEngine>();
            registration.AddGameEngine<TestEngine>();
        }

        public RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class NoEngineModule(string route) : IGameModule
    {
        public IPluginManifest Manifest { get; } = MakeManifest(route);

        public void RegisterServices(IPluginRegistration registration)
        {
            // Intentionally does NOT call AddGameEngine<T>.
        }

        public RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class TestEngine : AbstractGameEngine
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
            User host, CancellationToken ct = default)
            => throw new NotImplementedException();

        public override Task<Result> StartAsync(
            User host, AbstractGameState state, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class TempStoragePathService : IStoragePathService
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "knockbox-logicreg-tests", Guid.NewGuid().ToString("N"));
        public string GetAdminDirectory() => Path.Combine(_root, "admin");
        public string GetLogDirectory() => Path.Combine(_root, "logs");
        public string GetFirstPartyPluginsDirectory() => Path.Combine(_root, "games");
        public string GetExternalPluginsDirectory() => Path.Combine(_root, "external-games");
        public string GetPluginDataDirectory(string routeIdentifier) => Path.Combine(_root, "plugins", routeIdentifier);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
