using System.Collections.Frozen;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Platform.Games;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Filtering;
using KnockBox.Core.Services.Storage.ClientStorage;
using KnockBox.Platform.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Pins the denylist contract in <see cref="DefaultPluginRegistration"/>. The denylist
/// is the union of two sources:
/// <list type="bullet">
///   <item><see cref="DefaultPluginRegistration.AlwaysProtectedTypes"/> — static, covers
///     plugin-system primitives and Microsoft.Extensions fundamentals.</item>
///   <item>A per-construction snapshot of the host's <see cref="IServiceCollection"/>
///     at plugin-registration time — dynamic, self-maintaining as new platform
///     services get added.</item>
/// </list>
/// A denied registration must be dropped (no exception thrown — one bad call shouldn't
/// kill the plugin), must be logged at error level, and must leave the underlying
/// service collection unchanged. Plugin-private registrations still succeed.
/// </summary>
[TestClass]
public sealed class DefaultPluginRegistrationTests
{
    private static IPluginManifest Manifest() => new PluginManifest(
        Name: "Fixture",
        Description: "Fixture manifest.",
        RouteIdentifier: "fixture-plugin",
        Version: new Version(1, 0, 0),
        EntryAssembly: "Fixture.Assembly",
        Capabilities: new HashSet<PluginCapability>());

    /// <summary>
    /// Fresh service collection with a realistic "platform already registered these"
    /// snapshot baked in — mirrors what <c>LogicRegistrations</c> captures at runtime.
    /// </summary>
    private static (IServiceCollection services, FrozenSet<Type> snapshot) FreshWithHostServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProfanityFilter, FakeProfanityFilter>();
        services.AddSingleton<ILobbyCodeService, FakeLobbyCodeService>();
        var snapshot = DefaultPluginRegistration.CaptureHostOwnedServiceTypes(services);
        return (services, snapshot);
    }

    // Plugin-private types used for the "allowed" side of each overload.
    private interface IPluginService { }
    private sealed class PluginServiceImpl : IPluginService { }

    // Concrete stand-in for a host-owned service with a parameterless ctor so the
    // denylist tests don't need to satisfy constructor dependencies.
    private sealed class FakeProfanityFilter : IProfanityFilter
    {
        public ValueTask<ValueResult<List<ProfanityMatch>?>> ExtractProfanitiesAsync(string text, CancellationToken ct = default)
            => ValueTask.FromResult(ValueResult<List<ProfanityMatch>?>.FromValue(null));
    }

    private sealed class FakeLobbyCodeService : ILobbyCodeService
    {
        public int LobbyCodeLength => 6;
        public ValueTask<ValueResult<string>> IssueLobbyCodeAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
        public ValueTask<Result> ReleaseLobbyCodeAsync(string lobbyCode, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    // ─── Denylist catches snapshot-sourced host services ───────────────────

    [TestMethod]
    public void AddSingleton_SnapshotHostOwnedType_IsDropped()
    {
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance, snapshot);

        reg.AddSingleton<IProfanityFilter, FakeProfanityFilter>();

        // Registration must be dropped. Only the host's original registration remains.
        Assert.ContainsSingle(d => d.ServiceType == typeof(IProfanityFilter), services);
    }

    [TestMethod]
    public void AddScoped_SnapshotHostOwnedType_IsDropped()
    {
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance, snapshot);

        reg.AddScoped<ILobbyCodeService, FakeLobbyCodeService>();

        Assert.ContainsSingle(d => d.ServiceType == typeof(ILobbyCodeService), services);
    }

    [TestMethod]
    public void AddTransient_SnapshotHostOwnedType_IsDropped()
    {
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance, snapshot);

        reg.AddTransient<IProfanityFilter, FakeProfanityFilter>();

        Assert.ContainsSingle(d => d.ServiceType == typeof(IProfanityFilter), services);
    }

    [TestMethod]
    public void AddSingleton_Factory_SnapshotHostOwnedType_IsDropped()
    {
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance, snapshot);

        reg.AddSingleton<IProfanityFilter>(_ => new FakeProfanityFilter());

        Assert.ContainsSingle(d => d.ServiceType == typeof(IProfanityFilter), services);
    }

    // ─── Denylist catches always-protected types regardless of snapshot ────

    [TestMethod]
    public void AddSingleton_PluginSystemPrimitive_IsDropped_EvenWithoutSnapshot()
    {
        // AbstractGameEngine is in AlwaysProtectedTypes; even with an empty snapshot
        // the registration must be dropped. Plugins must route through AddGameEngine<T>().
        var services = new ServiceCollection();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance);

        reg.AddSingleton<AbstractGameEngine, DummyEngine>();

        Assert.DoesNotContain(d => d.ServiceType == typeof(AbstractGameEngine), services);
    }

    [TestMethod]
    public void AddSingleton_LoggerGeneric_IsDropped_EvenWithoutSnapshot()
    {
        // ILogger<> is AlwaysProtectedTypes (as an open generic); the check side
        // reduces closed generics to their open definition before matching.
        var services = new ServiceCollection();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance);

        reg.AddSingleton<ILogger<PluginServiceImpl>, FakeLogger>();

        Assert.DoesNotContain(d => d.ServiceType == typeof(ILogger<PluginServiceImpl>), services);
    }

    [TestMethod]
    public void DroppedRegistration_LogsError()
    {
        var logger = new Mock<ILogger>();
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), logger.Object, snapshot);

        reg.AddSingleton<IProfanityFilter, FakeProfanityFilter>();

        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    // ─── Plugin-private services still succeed ─────────────────────────────

    [TestMethod]
    public void AddSingleton_PluginPrivateType_IsRegistered()
    {
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance, snapshot);

        reg.AddSingleton<IPluginService, PluginServiceImpl>();

        Assert.Contains(d => d.ServiceType == typeof(IPluginService), services);
    }

    [TestMethod]
    public void AddGameEngine_AlwaysRegisters()
    {
        var (services, snapshot) = FreshWithHostServices();
        var reg = new DefaultPluginRegistration(services, Manifest(), NullLogger.Instance, snapshot);

        reg.AddGameEngine<DummyEngine>();

        Assert.Contains(d => d.ServiceType == typeof(DummyEngine), services,
            "Concrete engine must be registered as a singleton.");
        Assert.Contains(
            d => d.ServiceType == typeof(AbstractGameEngine) && d.IsKeyedService, services,
            "Keyed AbstractGameEngine registration must also be present.");
        Assert.AreEqual(1, reg.GameEngineRegistrationCount);
    }

    // ─── Snapshot behavior ─────────────────────────────────────────────────

    [TestMethod]
    public void CaptureHostOwnedServiceTypes_IncludesOpenGenericForClosedGenericRegistrations()
    {
        // When a host registers ILogger<HostService> (closed) without also registering
        // ILogger<> (open), plugins trying to register ILogger<AnythingElse> would
        // reduce to ILogger<> and miss the match. The snapshot captures both forms.
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<DummyEngine>, FakeLogger>();

        var snapshot = DefaultPluginRegistration.CaptureHostOwnedServiceTypes(services);

        Assert.Contains(typeof(ILogger<DummyEngine>), snapshot);
        Assert.Contains(typeof(ILogger<>), snapshot);
    }

    private sealed class DummyEngine : AbstractGameEngine
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
            => throw new NotImplementedException();

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    // ─── Per-plugin storage wiring: Create<T> auto-injects the keyed context ──

    [TestMethod]
    public async Task AddScoped_ContextTakingStorageService_ResolvesRouteScoped()
    {
        // Mirrors the runtime wiring the {Plugin}Storage services depend on:
        // LogicRegistrations registers the plugin's IPluginContext as a keyed
        // singleton, the plugin registers a context-taking service via
        // AddScoped<TStore,TStore>(), and Create<T> must inject the right
        // keyed context. This proves the architecture end-to-end: resolving the
        // store yields an instance that namespaces writes by the plugin's route.
        var services = new ServiceCollection();
        var manifest = Manifest(); // RouteIdentifier "fixture-plugin"
        var fakeLocal = new RecordingLocalStorage();
        services.AddSingleton<ILocalStorageService>(fakeLocal);
        services.AddKeyedSingleton<IPluginContext>(
            manifest.RouteIdentifier,
            (_, _) => new StubPluginContext(manifest));

        var reg = new DefaultPluginRegistration(services, manifest, NullLogger.Instance);
        reg.AddScoped<FixturePluginStorage, FixturePluginStorage>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<FixturePluginStorage>();

        await store.Local.SetAsync("settings", "value", 1);

        Assert.ContainsSingle(k => k == "fixture-plugin::settings.value", fakeLocal.WrittenKeys);
    }

    // Stand-in for a plugin's storage service: ctor takes IPluginContext (so
    // Create<T> must supply the keyed context) plus the raw ILocalStorageService,
    // and wraps the latter in the route-scoping wrapper — exactly the pattern
    // every {Plugin}Storage type uses.
    private sealed class FixturePluginStorage
    {
        public FixturePluginStorage(IPluginContext context, ILocalStorageService localStorage)
        {
            Local = new KnockBox.Core.Services.Storage.ClientStorage.ScopedClientStorageService(
                localStorage, context.Manifest.RouteIdentifier);
        }

        public IClientStorageService Local { get; }
    }

    private sealed class StubPluginContext(IPluginManifest manifest) : IPluginContext
    {
        public IPluginManifest Manifest { get; } = manifest;
        public ILogger Logger { get; } = NullLogger.Instance;
        public IConfiguration Configuration => throw new NotSupportedException();
        public IPluginStorage Storage => throw new NotSupportedException();
    }

    // Captures the physical keys written so the test can assert route prefixing.
    private sealed class RecordingLocalStorage : ILocalStorageService
    {
        public List<string> WrittenKeys { get; } = [];

        public ValueTask<Result> SetAsync<T>(string scope, string key, T value, CancellationToken ct = default)
        {
            WrittenKeys.Add($"{scope}.{key}");
            return new(Result.Success);
        }

        public ValueTask<ValueResult<T?>> GetAsync<T>(string scope, string key, CancellationToken ct = default)
            => new(ValueResult<T?>.FromValue(default));
        public ValueTask<Result> RemoveAsync(string scope, string key) => new(Result.Success);
        public ValueTask<Result> RemoveAsync(string scope) => new(Result.Success);
        public ValueTask<ValueResult<List<string>>> GetKeysAsync(string scope, CancellationToken ct = default)
            => new(ValueResult<List<string>>.FromValue([]));
        public ValueTask<ValueResult<List<string>>> GetAllKeysAsync(CancellationToken ct = default)
            => new(ValueResult<List<string>>.FromValue([]));
        public ValueTask<Result> ClearAsync() => new(Result.Success);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLogger : ILogger<PluginServiceImpl>, ILogger<DummyEngine>
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
