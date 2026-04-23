// -----------------------------------------------------------------------------
// Plugin entry point.
//
// The KnockBox host's PluginLoader reads plugin.json from this plugin's folder,
// loads the assembly, reflects for a public parameterless-ctor IGameModule
// whose Manifest.RouteIdentifier matches the manifest on disk, and invokes
// RegisterServices during DI build. Afterwards it asks GetButtonContent() for
// the tile shown on the home page.
//
// You rarely change this file after the first day: edit plugin.json for
// identity (name, description, route, version, capabilities), wire one
// AddGameEngine<T>() call inside RegisterServices, and point GetButtonContent
// at your tile component.
// -----------------------------------------------------------------------------

using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using MyGame.Components;

namespace MyGame;

/// <summary>
/// The single <see cref="IGameModule"/> implementation for this plugin. Exactly
/// one <c>IGameModule</c> whose <see cref="IPluginManifest.RouteIdentifier"/>
/// matches the on-disk <c>plugin.json</c> is required per plugin folder; the
/// host skips the plugin with an error if that invariant is violated.
/// </summary>
/// <remarks>
/// Must have a <b>public parameterless constructor</b>. The host activates it
/// via reflection before DI is built, so the ctor cannot take dependencies.
/// Do dependency-wiring inside <see cref="RegisterServices"/>, which receives
/// a narrow <see cref="IPluginRegistration"/> handle instead of the host's
/// raw <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </remarks>
public class MyGameModule : IGameModule
{
    /// <summary>
    /// The plugin's manifest, read from the embedded <c>plugin.json</c>. The
    /// on-disk copy (staged next to the plugin DLL in <c>games/</c>) and this
    /// embedded copy come from the same source file and must agree — the
    /// loader cross-checks them and rejects the plugin on any mismatch.
    /// </summary>
    public IPluginManifest Manifest { get; } =
        PluginManifest.FromEmbeddedResourceOrThrow(typeof(MyGameModule).Assembly);

    /// <summary>
    /// Called by the host during DI construction. Register any services your
    /// game needs (engine, repositories, background workers, etc.) here via
    /// the narrow <see cref="IPluginRegistration"/> surface. Host-owned
    /// services are invisible: you can only register plugin-private services
    /// and exactly one game engine.
    /// </summary>
    public void RegisterServices(IPluginRegistration registration)
    {
        // AddGameEngine<TEngine>() does two registrations, both scoped to this
        // plugin's own route identifier (from plugin.json):
        //   1) TEngine is registered as a singleton, so Razor pages can inject
        //      the concrete type directly (e.g. [Inject] MyGameGameEngine Engine).
        //   2) The same instance is re-exposed as a keyed AbstractGameEngine
        //      under this route identifier, so the platform's LobbyService can
        //      resolve it generically via GetKeyedService<AbstractGameEngine>(route).
        registration.AddGameEngine<MyGameGameEngine>();
    }

    /// <summary>
    /// Returns the inner content of this game's tile on the home page. The host
    /// owns the surrounding <c>&lt;button&gt;</c> (click handler, disabled state,
    /// aria-label, layout). This fragment owns the visual design — artwork,
    /// typography, animations — that distinguishes the game from other tiles.
    /// </summary>
    public RenderFragment GetButtonContent() => builder =>
    {
        // Replace MyGameTile with whatever Razor component you want rendered
        // inside the home-page tile. Scoped CSS on that component ships as
        // wwwroot/{PluginName}.styles.css and is served from
        // /_content/{PluginName}/{PluginName}.styles.css by the platform.
        builder.OpenComponent<MyGameTile>(0);
        builder.CloseComponent();
    };
}
