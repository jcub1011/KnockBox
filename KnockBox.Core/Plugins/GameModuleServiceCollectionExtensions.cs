using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Helpers for <see cref="IGameModule"/> implementations to register their engines
    /// consistently with the DI container.
    /// </summary>
    public static class GameModuleServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <typeparamref name="TEngine"/> as a singleton and exposes the same
        /// instance as a keyed <see cref="AbstractGameEngine"/> under <paramref name="routeIdentifier"/>.
        /// Registering under both shapes lets pages resolve the concrete engine directly
        /// while the lobby service resolves by route key, without instantiating twice.
        /// </summary>
        public static IServiceCollection AddGameEngine<TEngine>(
            this IServiceCollection services,
            string routeIdentifier)
            where TEngine : AbstractGameEngine
        {
            services.AddSingleton<TEngine>();
            services.AddKeyedSingleton<AbstractGameEngine>(
                routeIdentifier,
                (sp, _) => sp.GetRequiredService<TEngine>());
            return services;
        }
    }
}
