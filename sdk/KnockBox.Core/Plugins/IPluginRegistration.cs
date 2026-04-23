using KnockBox.Core.Services.Logic.Games.Engines.Shared;

namespace KnockBox.Core.Plugins;

/// <summary>
/// The sole surface a plugin uses to register DI services. Replaces the raw
/// <c>IServiceCollection</c> the host used to hand plugins directly. Every
/// registration is tracked as plugin-owned and cannot target host-owned
/// services, replace other plugins' registrations, or register an engine
/// under any key but the plugin's own <see cref="IPluginManifest.RouteIdentifier"/>.
/// </summary>
public interface IPluginRegistration
{
    /// <summary>The plugin's manifest, usable from factory overloads below.</summary>
    IPluginManifest Manifest { get; }

    /// <summary>
    /// Registers <typeparamref name="TEngine"/> as a singleton and as a keyed
    /// <see cref="AbstractGameEngine"/> under the plugin's own route identifier.
    /// Both registrations resolve to the same instance. MUST be called exactly
    /// once per plugin; zero or multiple calls are flagged as an error by the
    /// loader and the plugin is marked unreachable.
    /// </summary>
    void AddGameEngine<TEngine>() where TEngine : AbstractGameEngine;

    /// <summary>Registers a plugin-private singleton service.</summary>
    void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    /// <summary>Registers a plugin-private scoped service.</summary>
    void AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    /// <summary>Registers a plugin-private transient service.</summary>
    void AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    /// <summary>
    /// Registers a plugin-private singleton built from the plugin's
    /// <see cref="IPluginContext"/> (logger, config, storage).
    /// </summary>
    void AddSingleton<TService>(Func<IPluginContext, TService> factory) where TService : class;

    /// <summary>
    /// Registers a plugin-private scoped service built from the plugin's
    /// <see cref="IPluginContext"/>.
    /// </summary>
    void AddScoped<TService>(Func<IPluginContext, TService> factory) where TService : class;

    /// <summary>
    /// Registers a plugin-private transient service built from the plugin's
    /// <see cref="IPluginContext"/>.
    /// </summary>
    void AddTransient<TService>(Func<IPluginContext, TService> factory) where TService : class;
}
