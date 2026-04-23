using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Represents a game plugin discovered at runtime by <c>PluginLoader</c>.
/// </summary>
/// <remarks>
/// The set of <see cref="IGameModule"/> instances resolved from the DI
/// container is fixed at host startup (populated by
/// <c>PluginLoader.LoadModules</c> during <c>RegisterLogic</c>) and is not
/// mutated afterward. Callers that cache a projection of the module list — the
/// home page's alphabetically-sorted tile list, the admin dashboard's per-game
/// row list — rely on this invariant to skip re-sorting on every render. If
/// plugin hot-reload is ever introduced, those caches will need explicit
/// invalidation.
/// </remarks>
public interface IGameModule
{
    /// <summary>
    /// The plugin's manifest. Must agree with the <c>plugin.json</c> the loader
    /// parsed from disk; mismatched manifests cause the loader to reject the
    /// plugin. A plugin almost always populates this by reading its own
    /// embedded <c>plugin.json</c> via
    /// <see cref="PluginManifest.FromEmbeddedResource(System.Reflection.Assembly)"/>.
    /// </summary>
    IPluginManifest Manifest { get; }

    /// <summary>
    /// Registers plugin-owned services. The only way to register the game's
    /// engine, plus any plugin-private helpers. The host never hands plugins a
    /// raw <c>IServiceCollection</c>.
    /// </summary>
    void RegisterServices(IPluginRegistration registration);

    /// <summary>
    /// Returns the inner content rendered inside the game's tile button on the
    /// Home screen. The host owns the surrounding <c>&lt;button&gt;</c> wrapper
    /// (click handler, disabled state, aria-label, layout sizing); this
    /// fragment owns the visual design that distinguishes the game from other
    /// tiles.
    /// </summary>
    RenderFragment GetButtonContent();

    /// <summary>
    /// Optionally returns a custom header fragment rendered inside the host's
    /// <c>&lt;header&gt;</c> element while the user is inside this game's
    /// room. Return <c>null</c> (the default) to inherit the host's built-in
    /// header (game name link, room code button, leave button). The host owns
    /// the <c>&lt;header&gt;</c> wrapper (animation classes, layout, shadow);
    /// this fragment owns everything rendered inside it.
    /// </summary>
    RenderFragment? GetCustomHeader() => null;
}
