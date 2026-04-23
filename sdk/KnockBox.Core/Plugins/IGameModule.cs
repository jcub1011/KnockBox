using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Represents a game module that can be dynamically loaded into the KnockBox platform.
    /// </summary>
    /// <remarks>
    /// The set of <see cref="IGameModule"/> instances resolved from the DI container is fixed
    /// at host startup (populated by <c>PluginLoader.LoadModules</c> during
    /// <c>RegisterLogic</c>) and is not mutated afterward. Callers that cache a projection
    /// of the module list — for example, the home page's alphabetically-sorted tile list and
    /// the admin dashboard's per-game row list — rely on this invariant to skip re-sorting on
    /// every render. If plugin hot-reload is ever introduced, those caches will need explicit
    /// invalidation.
    /// </remarks>
    public interface IGameModule
    {
        /// <summary>
        /// The display name of the game.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A brief description of the game.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The unique route identifier used for navigation and lobby creation (e.g., "card-counter").
        /// </summary>
        string RouteIdentifier { get; }

        /// <summary>
        /// Registers game-specific services into the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        void RegisterServices(IServiceCollection services);

        /// <summary>
        /// Returns the inner content rendered inside the game's tile button on the Home screen.
        /// The host owns the surrounding <c>&lt;button&gt;</c> wrapper (click handler, disabled state,
        /// aria-label, layout sizing); this fragment owns the visual design that distinguishes the
        /// game from other tiles.
        /// </summary>
        RenderFragment GetButtonContent();

        /// <summary>
        /// Optionally returns a custom header fragment rendered inside the host's <c>&lt;header&gt;</c>
        /// element while the user is inside this game's room. Return <c>null</c> (the default) to
        /// inherit the host's built-in header (game name link, room code button, leave button).
        /// The host owns the <c>&lt;header&gt;</c> wrapper (animation classes, layout, shadow);
        /// this fragment owns everything rendered inside it.
        /// </summary>
        RenderFragment? GetCustomHeader() => null;
    }
}
