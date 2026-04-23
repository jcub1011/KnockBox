using System.Reflection;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Registrations.Logic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Focused coverage for the DI-key collision detection added to
/// <see cref="LogicRegistrations.RegisterLogic"/>. A plugin's
/// <c>RegisterServices</c> callback runs with full DI authority — the
/// platform cannot prevent it from calling <c>AddKeyedSingleton&lt;AbstractGameEngine&gt;(...)</c>
/// with any key. RegisterLogic's job is to detect misuse, drop the offending
/// descriptor, and log at Error so the misbehaving plugin surfaces at startup.
/// </summary>
[TestClass]
public sealed class LogicRegistrationsTests
{
    [TestMethod]
    public void Preserves_LegitimateRegistrations_ViaHelper()
    {
        var services = new ServiceCollection();
        var logger = NullLogger.Instance;

        var module = new GoodModule("good");
        services.RegisterLogic(new PluginLoadResult([module], []), logger);

        var provider = services.BuildServiceProvider();

        var keyed = provider.GetKeyedService<AbstractGameEngine>("good");
        Assert.IsNotNull(keyed, "The legitimate keyed engine must survive RegisterLogic.");
        Assert.IsInstanceOfType<TestEngine>(keyed);

        var concrete = provider.GetService<TestEngine>();
        Assert.IsNotNull(concrete, "The concrete singleton registered by AddGameEngine must also survive.");
        Assert.AreSame(keyed, concrete, "Keyed and concrete registrations must resolve to the same instance.");
    }

    [TestMethod]
    public void Drops_KeyedEngine_RegisteredUnderWrongRoute()
    {
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        // The module claims route "a" but registers a keyed engine under "b".
        var module = new MaliciousModule("a", wrongKey: "b");
        services.RegisterLogic(new PluginLoadResult([module], []), logger);

        var provider = services.BuildServiceProvider();

        Assert.IsNull(
            provider.GetKeyedService<AbstractGameEngine>("b"),
            "The under-wrong-route registration must be dropped.");
        Assert.IsTrue(
            logger.Errors.Any(e => e.Contains("only its own RouteIdentifier", StringComparison.Ordinal)),
            "An Error must be logged explaining the mismatch.");
    }

    [TestMethod]
    public void Drops_KeyedEngine_WhenKeyAlreadyClaimed_ByEarlierPlugin()
    {
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        // Two plugins claiming the same RouteIdentifier. PluginLoader dedupes
        // this up-stream, but the safety net in RegisterLogic still has to
        // handle it in case a future caller bypasses the loader.
        var first = new GoodModule("shared-route");
        var second = new GoodModule("shared-route");

        services.RegisterLogic(new PluginLoadResult([first, second], []), logger);

        var provider = services.BuildServiceProvider();

        // The first plugin's registration survives.
        var keyed = provider.GetKeyedService<AbstractGameEngine>("shared-route");
        Assert.IsNotNull(keyed);

        Assert.IsTrue(
            logger.Errors.Any(e => e.Contains("already claimed", StringComparison.Ordinal)),
            "An Error must be logged explaining the prior-owner collision.");
    }

    [TestMethod]
    public void Drops_KeyedEngine_WithNonStringKey()
    {
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        var module = new IntKeyModule();
        services.RegisterLogic(new PluginLoadResult([module], []), logger);

        var provider = services.BuildServiceProvider();

        Assert.IsNull(
            provider.GetKeyedService<AbstractGameEngine>(42),
            "Non-string keys must be dropped.");
        Assert.IsTrue(
            logger.Errors.Any(e => e.Contains("non-string service key", StringComparison.Ordinal)),
            "An Error must be logged explaining the non-string key.");
    }

    [TestMethod]
    public void ThrowingPlugin_DoesNotBlock_LaterPluginWithSameRoute()
    {
        // If a plugin throws from RegisterServices, it aborts the loop iteration
        // *before* its RouteIdentifier is added to ownedKeys. A later plugin
        // with the same RouteIdentifier therefore registers successfully.
        var services = new ServiceCollection();
        var logger = new CapturingLogger();

        var throwing = new ThrowingModule("shared-route");
        var good = new GoodModule("shared-route");

        services.RegisterLogic(new PluginLoadResult([throwing, good], []), logger);

        var provider = services.BuildServiceProvider();

        var keyed = provider.GetKeyedService<AbstractGameEngine>("shared-route");
        Assert.IsNotNull(keyed,
            "A throwing plugin must not claim the route key; the next legitimate plugin registers normally.");
        Assert.IsInstanceOfType<TestEngine>(keyed);
    }

    // ── Fake modules & engines ───────────────────────────────────────────

    private sealed class GoodModule(string route) : IGameModule
    {
        public string Name => $"Good-{route}";
        public string Description => "Well-behaved test module.";
        public string RouteIdentifier => route;

        public void RegisterServices(IServiceCollection services)
            => services.AddGameEngine<TestEngine>(RouteIdentifier);

        public Microsoft.AspNetCore.Components.RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class MaliciousModule(string route, string wrongKey) : IGameModule
    {
        public string Name => $"Malicious-{route}";
        public string Description => "Registers a keyed engine under a route it doesn't own.";
        public string RouteIdentifier => route;

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<TestEngine>();
            services.AddKeyedSingleton<AbstractGameEngine>(
                wrongKey,
                (sp, _) => sp.GetRequiredService<TestEngine>());
        }

        public Microsoft.AspNetCore.Components.RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class IntKeyModule : IGameModule
    {
        public string Name => "IntKey";
        public string Description => "Registers a keyed engine with a non-string key.";
        public string RouteIdentifier => "int-key";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<TestEngine>();
            services.AddKeyedSingleton<AbstractGameEngine>(
                42,
                (sp, _) => sp.GetRequiredService<TestEngine>());
        }

        public Microsoft.AspNetCore.Components.RenderFragment GetButtonContent() => _ => { };
    }

    private sealed class ThrowingModule(string route) : IGameModule
    {
        public string Name => $"Throwing-{route}";
        public string Description => "Throws from RegisterServices.";
        public string RouteIdentifier => route;

        public void RegisterServices(IServiceCollection services)
            => throw new InvalidOperationException("intentional");

        public Microsoft.AspNetCore.Components.RenderFragment GetButtonContent() => _ => { };
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
