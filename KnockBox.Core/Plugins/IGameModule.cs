using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Represents a game module that can be dynamically loaded into the KnockBox platform.
    /// </summary>
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
    }
}
