using System.Collections.Concurrent;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// <see cref="IPluginRegistration"/> implementation handed to each plugin's
/// <c>RegisterServices</c>. Wraps the real <see cref="IServiceCollection"/>,
/// restricts the plugin to plugin-private registrations only, and records the
/// <see cref="AddGameEngine{TEngine}"/> call count so the host can flag
/// plugins that register zero or &gt;1 engines.
/// </summary>
/// <remarks>
/// The plugin's <see cref="IPluginContext"/> is registered as a keyed
/// singleton elsewhere (keyed by <see cref="IPluginManifest.RouteIdentifier"/>)
/// and resolved lazily inside each factory below. This keeps context
/// construction dependent on already-registered host services (logger factory,
/// configuration, storage paths) without needing a premature
/// <c>BuildServiceProvider</c>. <see cref="ActivatorUtilities.CreateInstance"/>
/// only accepts override parameters whose types match a ctor parameter, so we
/// pre-inspect the target type and omit the context override when the ctor
/// does not declare it.
/// </remarks>
internal sealed class DefaultPluginRegistration(
    IServiceCollection services,
    IPluginManifest manifest) : IPluginRegistration
{
    private static readonly ConcurrentDictionary<Type, bool> CtorTakesContextCache = new();

    public IPluginManifest Manifest { get; } = manifest;

    /// <summary>
    /// How many times the plugin called <see cref="AddGameEngine{TEngine}"/>.
    /// The caller validates this is exactly <c>1</c> after
    /// <see cref="IGameModule.RegisterServices"/> returns.
    /// </summary>
    public int GameEngineRegistrationCount { get; private set; }

    public void AddGameEngine<TEngine>() where TEngine : AbstractGameEngine
    {
        GameEngineRegistrationCount++;

        services.AddSingleton<TEngine>(sp => Create<TEngine>(sp));
        services.AddKeyedSingleton<AbstractGameEngine>(
            Manifest.RouteIdentifier,
            (sp, _) => sp.GetRequiredService<TEngine>());
    }

    public void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
        => services.AddSingleton<TService>(sp => Create<TImplementation>(sp));

    public void AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
        => services.AddScoped<TService>(sp => Create<TImplementation>(sp));

    public void AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
        => services.AddTransient<TService>(sp => Create<TImplementation>(sp));

    public void AddSingleton<TService>(Func<IPluginContext, TService> factory) where TService : class
        => services.AddSingleton<TService>(sp => factory(ResolveContext(sp)));

    public void AddScoped<TService>(Func<IPluginContext, TService> factory) where TService : class
        => services.AddScoped<TService>(sp => factory(ResolveContext(sp)));

    public void AddTransient<TService>(Func<IPluginContext, TService> factory) where TService : class
        => services.AddTransient<TService>(sp => factory(ResolveContext(sp)));

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
