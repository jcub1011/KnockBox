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
        /// <para>
        /// Both shapes are intentional and serve different callers:
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     The keyed <see cref="AbstractGameEngine"/> registration is for the host
        ///     (lobby/router), which has no compile-time knowledge of concrete engine
        ///     types and resolves engines by route key.
        ///   </item>
        ///   <item>
        ///     The concrete <typeparamref name="TEngine"/> registration is for the
        ///     plugin's own Razor pages, which know the concrete type and can inject
        ///     it directly without <c>[FromKeyedServices]</c> plumbing.
        ///   </item>
        /// </list>
        /// <para>
        /// The keyed registration resolves through the concrete registration, so a
        /// single instance is shared across both shapes -- no double-construction.
        /// </para>
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
