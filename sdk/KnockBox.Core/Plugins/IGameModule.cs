using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Represents a user-facing game plugin discovered at runtime by
/// <c>PluginLoader</c>. Inherits <see cref="IPluginModule.Manifest"/> and
/// <see cref="IPluginModule.RegisterServices"/> from the base interface; only
/// game-tile-specific overrides live here.
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
public interface IGameModule : IPluginModule
{
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
