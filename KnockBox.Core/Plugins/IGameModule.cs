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
    }
}
