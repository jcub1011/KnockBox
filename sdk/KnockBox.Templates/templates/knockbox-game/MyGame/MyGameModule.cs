// -----------------------------------------------------------------------------
// Plugin entry point.
//
// The KnockBox host's PluginLoader reflects over every assembly in its games/
// folder looking for a public, parameterless-ctor IGameModule implementation.
// It finds this class, instantiates it, calls RegisterServices during DI build,
// and later asks GetButtonContent() for the tile shown on the home page.
//
// You rarely change this file after the first day: set Name / Description /
// RouteIdentifier, wire one AddGameEngine<T>() call, and point GetButtonContent
// at your tile component.
// -----------------------------------------------------------------------------

using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGame.Components;

namespace MyGame;

/// <summary>
/// The single <see cref="IGameModule"/> implementation for this plugin. Exactly
/// one concrete <c>IGameModule</c> is required per plugin assembly; the host
/// fails fast if it finds zero or more than one.
/// </summary>
/// <remarks>
/// Must have a <b>public parameterless constructor</b>. The host activates it
/// via reflection before DI is built, so the ctor cannot take dependencies.
/// Do dependency-wiring inside <see cref="RegisterServices"/> instead.
/// </remarks>
public class MyGameModule : IGameModule
{
    /// <summary>Display name shown on the home-page tile and in the game header.</summary>
    public string Name => "My Game";

    /// <summary>One-line description shown on the home-page tile.</summary>
    public string Description => "A KnockBox party game.";

    /// <summary>
    /// URL-safe route identifier. <b>Must match exactly</b> the middle segment of
    /// the <c>@page</c> directive on every page in this plugin (e.g.,
    /// <c>@page "/room/my-game/{ObfuscatedRoomCode}"</c>). A mismatch surfaces as
    /// a 404 when a player tries to join or create a lobby for this game.
    /// </summary>
    /// <remarks>
    /// The <c>routeIdentifier</c> template parameter replaces this literal at
    /// scaffolding time and also replaces it in each page's <c>@page</c> route.
    /// After scaffolding, keep the two in sync manually.
    /// </remarks>
    public string RouteIdentifier => "my-game";

    /// <summary>
    /// Called by the host during DI construction. Register any services your
    /// game needs (engine, repositories, background workers, etc.) here.
    /// </summary>
    /// <param name="services">The host's <see cref="IServiceCollection"/>.</param>
    public void RegisterServices(IServiceCollection services)
    {
        // AddGameEngine<TEngine>(routeIdentifier) does two registrations:
        //   1) TEngine is registered as a singleton, so Razor pages can inject the
        //      concrete type directly (e.g. [Inject] MyGameGameEngine Engine).
        //   2) The same instance is re-exposed as a keyed AbstractGameEngine
        //      under this route identifier, so the platform's LobbyService can
        //      resolve it generically via GetKeyedService<AbstractGameEngine>(route).
        services.AddGameEngine<MyGameGameEngine>(RouteIdentifier);
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
