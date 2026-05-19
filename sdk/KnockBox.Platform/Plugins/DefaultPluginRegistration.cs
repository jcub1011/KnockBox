using System.Collections.Concurrent;
using System.Collections.Frozen;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// <see cref="IPluginRegistration"/> implementation handed to each plugin's
/// <c>RegisterServices</c>. Wraps the real <see cref="IServiceCollection"/>,
/// restricts the plugin to plugin-private registrations only, and records the
/// <see cref="AddGameEngine{TEngine}"/> call count so the host can flag
/// plugins that register zero or &gt;1 engines.
/// </summary>
/// <remarks>
/// <para>The plugin's <see cref="IPluginContext"/> is registered as a keyed
/// singleton elsewhere (keyed by <see cref="IPluginManifest.RouteIdentifier"/>)
/// and resolved lazily inside each factory below. This keeps context
/// construction dependent on already-registered host services (logger factory,
/// configuration, storage paths) without needing a premature
/// <c>BuildServiceProvider</c>. <see cref="ActivatorUtilities.CreateInstance"/>
/// only accepts override parameters whose types match a ctor parameter, so we
/// pre-inspect the target type and omit the context override when the ctor
/// does not declare it.</para>
/// <para><b>Host-owned service denylist:</b> the set of service types a plugin
/// cannot register is a union of <see cref="AlwaysProtectedTypes"/> (small,
/// static, covering plugin-system primitives and Microsoft.Extensions
/// fundamentals) and a <i>snapshot</i> of the <see cref="IServiceCollection"/>
/// captured before the plugin loop starts. That snapshot is self-maintaining:
/// any host service registered before <c>RegisterLogic</c> is automatically
/// protected without requiring an explicit allow/deny list entry.</para>
/// </remarks>
internal sealed class DefaultPluginRegistration : IPluginRegistration
{
    private static readonly ConcurrentDictionary<Type, bool> CtorTakesContextCache = new();

    /// <summary>
    /// Service types that are always protected regardless of what's in the host's
    /// service collection at plugin-registration time. Two categories: plugin-system
    /// primitives (not registered as singletons before plugins load, so they wouldn't
    /// appear in a <see cref="IServiceCollection"/> snapshot), and Microsoft.Extensions
    /// fundamentals we explicitly don't want plugins to swap out even if a future
    /// refactor accidentally registers them after the plugin loop.
    /// </summary>
    internal static readonly FrozenSet<Type> AlwaysProtectedTypes = FrozenSet.ToFrozenSet(
    [
        // Plugin-system primitives — replacing any of these breaks the sandbox
        typeof(IPluginContext),
        typeof(IPluginRegistration),
        typeof(IPluginManifest),
        typeof(IPluginStorage),
        typeof(IPluginModule),
        typeof(IGameModule),
        typeof(ILibraryModule),
        typeof(AbstractGameEngine),
        typeof(AbstractGameState),

        // Microsoft.Extensions contracts plugins must never override. These are
        // typically already in the snapshot (WebApplicationBuilder wires them in)
        // but listing them here means a refactor that changes registration order
        // can't silently un-protect them.
        typeof(IConfiguration),
        typeof(IHostedService),
        typeof(IHostApplicationLifetime),
        typeof(ILoggerFactory),
        typeof(ILogger),
        typeof(ILogger<>),
    ]);

    private readonly IServiceCollection _services;
    private readonly ILogger? _logger;
    private readonly FrozenSet<Type> _hostOwnedServiceTypes;

    /// <summary>
    /// Constructs a plugin registration facade.
    /// </summary>
    /// <param name="services">The host's service collection. Plugin-private registrations land here.</param>
    /// <param name="manifest">The plugin's manifest, used for route-keyed context resolution.</param>
    /// <param name="logger">Logger that reports dropped (denied) registrations by plugin name and service type.</param>
    /// <param name="hostOwnedServiceTypes">
    /// Snapshot of service types registered before this plugin registered, taken by
    /// <c>LogicRegistrations.RegisterLogic</c>. Unioned with <see cref="AlwaysProtectedTypes"/>
    /// to form the effective denylist. Pass <c>null</c> in tests that only need the static
    /// always-protected set.
    /// </param>
    public DefaultPluginRegistration(
        IServiceCollection services,
        IPluginManifest manifest,
        ILogger? logger = null,
        FrozenSet<Type>? hostOwnedServiceTypes = null)
    {
        _services = services;
        _logger = logger;
        _hostOwnedServiceTypes = hostOwnedServiceTypes ?? FrozenSet<Type>.Empty;
        Manifest = manifest;
    }

    public IPluginManifest Manifest { get; }

    /// <summary>
    /// How many times the plugin called <see cref="AddGameEngine{TEngine}"/>.
    /// The caller validates this is exactly <c>1</c> after
    /// <see cref="IGameModule.RegisterServices"/> returns.
    /// </summary>
    public int GameEngineRegistrationCount { get; private set; }

    /// <summary>
    /// Builds a host-owned snapshot from the current service collection. Closed-generic
    /// service types (e.g. <c>ILogger&lt;Foo&gt;</c>) are included as-is; the check side
    /// also reduces closed generics to their open definition, so either shape suffices.
    /// </summary>
    /// <remarks>
    /// Re-snapshot cadence (see <c>LogicRegistrations.RegisterLogic</c>):
    /// <list type="bullet">
    ///   <item>Once before pass 1 starts — captures everything the host registered
    ///   so plugins can't shadow platform services.</item>
    ///   <item>Once before EACH library plugin in pass 1 — captures services
    ///   previously-loaded libraries registered, so library B can't shadow
    ///   library A.</item>
    ///   <item>Once at the end of pass 1 — captures all library-exported services
    ///   so game plugins in pass 2 can't shadow them.</item>
    ///   <item>Game pass keeps the once-before-the-loop semantics; two games
    ///   shadowing each other remains last-wins by design.</item>
    /// </list>
    /// </remarks>
    public static FrozenSet<Type> CaptureHostOwnedServiceTypes(IServiceCollection services)
    {
        var types = new HashSet<Type>();
        foreach (var descriptor in services)
        {
            types.Add(descriptor.ServiceType);
            // Also remember the open-generic form so a plugin trying to register
            // ILogger<SomethingElse> is blocked even if the host only registered
            // a specific closed generic like ILogger<HostService>.
            if (descriptor.ServiceType.IsGenericType && !descriptor.ServiceType.IsGenericTypeDefinition)
                types.Add(descriptor.ServiceType.GetGenericTypeDefinition());
        }
        return FrozenSet.ToFrozenSet(types);
    }

    public void AddGameEngine<TEngine>() where TEngine : AbstractGameEngine
    {
        GameEngineRegistrationCount++;

        _services.AddSingleton<TEngine>(sp => Create<TEngine>(sp));
        _services.AddKeyedSingleton<AbstractGameEngine>(
            Manifest.RouteIdentifier,
            (sp, _) => sp.GetRequiredService<TEngine>());
    }

    public void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (IsHostOwned(typeof(TService))) return;
        _services.AddSingleton<TService>(sp => Create<TImplementation>(sp));
    }

    public void AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (IsHostOwned(typeof(TService))) return;
        _services.AddScoped<TService>(sp => Create<TImplementation>(sp));
    }

    public void AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (IsHostOwned(typeof(TService))) return;
        _services.AddTransient<TService>(sp => Create<TImplementation>(sp));
    }

    public void AddSingleton<TService>(Func<IPluginContext, TService> factory) where TService : class
    {
        if (IsHostOwned(typeof(TService))) return;
        _services.AddSingleton<TService>(sp => factory(ResolveContext(sp)));
    }

    public void AddScoped<TService>(Func<IPluginContext, TService> factory) where TService : class
    {
        if (IsHostOwned(typeof(TService))) return;
        _services.AddScoped<TService>(sp => factory(ResolveContext(sp)));
    }

    public void AddTransient<TService>(Func<IPluginContext, TService> factory) where TService : class
    {
        if (IsHostOwned(typeof(TService))) return;
        _services.AddTransient<TService>(sp => factory(ResolveContext(sp)));
    }

    /// <summary>
    /// Returns true when <paramref name="serviceType"/> is in the deny-set: either
    /// the static <see cref="AlwaysProtectedTypes"/> or the per-construction
    /// <c>_hostOwnedServiceTypes</c> snapshot. Closed generics are reduced to their
    /// open definition before the check, so <c>ILogger&lt;PluginService&gt;</c> matches
    /// a host registration of <c>ILogger&lt;&gt;</c>.
    /// </summary>
    private bool IsHostOwned(Type serviceType)
    {
        var effective = serviceType.IsGenericType && !serviceType.IsGenericTypeDefinition
            ? serviceType.GetGenericTypeDefinition()
            : serviceType;

        bool blocked =
            AlwaysProtectedTypes.Contains(serviceType)
            || AlwaysProtectedTypes.Contains(effective)
            || _hostOwnedServiceTypes.Contains(serviceType)
            || _hostOwnedServiceTypes.Contains(effective);

        if (!blocked) return false;

        _logger?.LogError(
            "Plugin [{PluginName}] ({Route}) attempted to register host-owned service type [{ServiceType}]; registration dropped.",
            Manifest.Name,
            Manifest.RouteIdentifier,
            serviceType.FullName);
        return true;
    }

    private IPluginContext ResolveContext(IServiceProvider sp)
        => sp.GetRequiredKeyedService<IPluginContext>(Manifest.RouteIdentifier);

    private T Create<T>(IServiceProvider sp) where T : class
    {
        if (CtorTakesContextCache.GetOrAdd(typeof(T), CtorTakesContext))
            return ActivatorUtilities.CreateInstance<T>(sp, ResolveContext(sp));
        return ActivatorUtilities.CreateInstance<T>(sp);
    }

    private static bool CtorTakesContext(Type type) =>
        type.GetConstructors()
            .Any(c => c.GetParameters()
                .Any(p => p.ParameterType == typeof(IPluginContext)));
}
