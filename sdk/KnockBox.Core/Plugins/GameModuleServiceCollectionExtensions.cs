using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Internal helper for registering an engine as both a concrete singleton and
    /// a keyed <see cref="AbstractGameEngine"/>. Plugins never call this directly;
    /// <c>DefaultPluginRegistration.AddGameEngine&lt;T&gt;()</c> (in
    /// KnockBox.Platform) invokes it with the plugin's own route identifier.
    /// </summary>
    internal static class GameModuleServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <typeparamref name="TEngine"/> as a singleton and exposes the same
        /// instance as a keyed <see cref="AbstractGameEngine"/> under <paramref name="routeIdentifier"/>.
        /// The keyed registration resolves through the concrete registration, so a
        /// single instance is shared across both shapes — no double-construction.
        /// </summary>
        internal static IServiceCollection AddGameEngine<TEngine>(
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
