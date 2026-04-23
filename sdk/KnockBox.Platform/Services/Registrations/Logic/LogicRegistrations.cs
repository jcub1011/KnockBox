using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Platform.Filtering;
using KnockBox.Platform.Games;
using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services, PluginLoadResult pluginLoadResult, ILogger logger)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            // Track which route keys have been claimed by earlier plugins so a
            // later plugin can't shadow another plugin's engine registration.
            // Keys are compared OrdinalIgnoreCase to match PluginLoader's own
            // duplicate-route detection.
            var ownedKeys = new Dictionary<string, IGameModule>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in pluginLoadResult.Modules)
            {
                var snapshot = services.Count;

                try
                {
                    module.RegisterServices(services);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to register services for game module [{Name}] ({Type}); skipping.",
                        module.Name,
                        module.GetType().FullName);
                    continue;
                }

                ValidateKeyedEngineRegistrations(services, snapshot, module, ownedKeys, logger);
                ownedKeys[module.RouteIdentifier] = module;
                services.AddSingleton(typeof(IGameModule), module);
            }

            services.AddSingleton(new GamePluginAssemblies(pluginLoadResult.Assemblies));

            return services;
        }

        /// <summary>
        /// Inspects every new <see cref="ServiceDescriptor"/> a plugin's
        /// <c>RegisterServices</c> appended to the collection. Any keyed
        /// <see cref="AbstractGameEngine"/> registration whose key doesn't match
        /// the plugin's <see cref="IGameModule.RouteIdentifier"/>, or whose key
        /// has already been claimed by an earlier plugin, is removed and an
        /// error is logged. Plugins that use the
        /// <c>AddGameEngine&lt;T&gt;(RouteIdentifier)</c> helper are unaffected.
        /// </summary>
        private static void ValidateKeyedEngineRegistrations(
            IServiceCollection services,
            int snapshot,
            IGameModule module,
            IReadOnlyDictionary<string, IGameModule> ownedKeys,
            ILogger logger)
        {
            // Collect offending indices first so we can remove them back-to-front
            // without invalidating earlier indices.
            var toRemove = new List<int>();

            for (var i = snapshot; i < services.Count; i++)
            {
                var descriptor = services[i];
                if (descriptor.ServiceType != typeof(AbstractGameEngine))
                    continue;
                if (!descriptor.IsKeyedService)
                    continue;

                var key = descriptor.ServiceKey as string;

                if (key is null)
                {
                    logger.LogError(
                        "Plugin [{Name}] ({Type}) registered an AbstractGameEngine with a non-string service key [{Key}]; " +
                        "only the plugin's own RouteIdentifier is allowed. Dropping the registration.",
                        module.Name,
                        module.GetType().FullName,
                        descriptor.ServiceKey);
                    toRemove.Add(i);
                    continue;
                }

                if (!string.Equals(key, module.RouteIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError(
                        "Plugin [{Name}] ({Type}) registered an AbstractGameEngine under key [{Key}], " +
                        "but only its own RouteIdentifier [{RouteIdentifier}] is allowed. Dropping the registration.",
                        module.Name,
                        module.GetType().FullName,
                        key,
                        module.RouteIdentifier);
                    toRemove.Add(i);
                    continue;
                }

                if (ownedKeys.TryGetValue(key, out var priorOwner))
                {
                    logger.LogError(
                        "Plugin [{Name}] ({Type}) registered an AbstractGameEngine under key [{Key}], " +
                        "already claimed by plugin [{PriorName}] ({PriorType}). Dropping the registration.",
                        module.Name,
                        module.GetType().FullName,
                        key,
                        priorOwner.Name,
                        priorOwner.GetType().FullName);
                    toRemove.Add(i);
                }
            }

            for (var i = toRemove.Count - 1; i >= 0; i--)
                services.RemoveAt(toRemove[i]);
        }
    }
}
