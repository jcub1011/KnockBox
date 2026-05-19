using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Platform.Filtering;
using KnockBox.Platform.Games;
using KnockBox.Platform.Plugins;
using KnockBox.Platform.Storage;
using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        /// <summary>
        /// Runs the host's plugin-registration pipeline in two passes — libraries
        /// first, then games — with snapshot-based shadowing protection at every
        /// transition. See the <c>LogicRegistrations.RegisterLogic</c> section of
        /// the architecture doc for the full reasoning.
        /// </summary>
        public static IServiceCollection RegisterLogic(this IServiceCollection services, PluginLoadResult pluginLoadResult, ILogger logger)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            // ─── Pass 1: library plugins ────────────────────────────────────────
            //
            // For each library, the host-owned snapshot is re-captured immediately
            // before that library's RegisterServices runs. That way, services
            // previously-loaded libraries registered are protected from shadowing
            // by subsequent libraries: library B's attempt to AddSingleton<IFoo,…>
            // when library A already registered IFoo is dropped + logged by
            // DefaultPluginRegistration.IsHostOwned.
            //
            // The very first snapshot (before library #1 starts) captures every
            // platform service the host registered above; library plugins can't
            // shadow those either.

            foreach (var plugin in pluginLoadResult.Plugins)
            {
                if (plugin.Module is not ILibraryModule)
                    continue;

                // Per-library snapshot rebuild: a library that registers a service
                // adds it to `services`, so the next iteration's snapshot will
                // include it and the next library cannot shadow it.
                var perLibrarySnapshot = DefaultPluginRegistration.CaptureHostOwnedServiceTypes(services);

                RegisterOnePlugin(services, plugin, perLibrarySnapshot, logger, isLibrary: true);

                services.AddSingleton(typeof(ILibraryModule), (ILibraryModule)plugin.Module);
            }

            // ─── Pass 2: game plugins ───────────────────────────────────────────
            //
            // Single snapshot taken at the end of pass 1. This includes every
            // service registered by any library plugin, so game plugins cannot
            // shadow library-exported services. The same snapshot is reused
            // across all game plugins — keeping the current "two games can
            // shadow each other" semantics that the existing pipeline had.

            var postLibrarySnapshot = DefaultPluginRegistration.CaptureHostOwnedServiceTypes(services);

            foreach (var plugin in pluginLoadResult.Plugins)
            {
                if (plugin.Module is not IGameModule gameModule)
                    continue;

                RegisterOnePlugin(services, plugin, postLibrarySnapshot, logger, isLibrary: false);

                // The module is still exposed as IGameModule so the home page
                // and admin dashboard can enumerate it even if the engine
                // registration was malformed.
                services.AddSingleton(typeof(IGameModule), gameModule);
            }

            services.AddSingleton(new GamePluginAssemblies(pluginLoadResult.Assemblies));

            return services;
        }

        /// <summary>
        /// Registers a single plugin's services using a freshly-constructed
        /// <see cref="DefaultPluginRegistration"/>. Extracted so both passes share
        /// the same error handling, context registration, and engine-count
        /// assertion logic; the <paramref name="isLibrary"/> flag tunes the
        /// engine-count expectation (libraries must call AddGameEngine zero
        /// times; games exactly once).
        /// </summary>
        private static void RegisterOnePlugin(
            IServiceCollection services,
            LoadedPlugin plugin,
            System.Collections.Frozen.FrozenSet<System.Type> hostOwnedSnapshot,
            ILogger logger,
            bool isLibrary)
        {
            var manifest = plugin.Manifest;
            var route = manifest.RouteIdentifier;

            // Register the plugin's IPluginContext as a keyed singleton so
            // DefaultPluginRegistration's factories can resolve it lazily
            // once the rest of the host's services (logger factory,
            // configuration, storage paths) are built.
            services.AddKeyedSingleton<IPluginContext>(route, (sp, _) =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var configuration = sp.GetRequiredService<IConfiguration>();
                var storagePaths = sp.GetRequiredService<IStoragePathService>();

                var pluginLogger = loggerFactory.CreateLogger($"Plugins.{route}");
                var pluginConfig = configuration.GetSection($"Plugins:{route}");
                var pluginStorage = new DefaultPluginStorage(storagePaths.GetPluginDataDirectory(route));

                return new DefaultPluginContext(manifest, pluginLogger, pluginConfig, pluginStorage);
            });

            var registration = new DefaultPluginRegistration(services, manifest, logger, hostOwnedSnapshot);

            try
            {
                plugin.Module.RegisterServices(registration);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to register services for plugin [{Name}] ({Type}); skipping.",
                    manifest.Name,
                    plugin.Module.GetType().FullName);
                return;
            }

            var expectedEngineCount = isLibrary ? 0 : 1;
            if (registration.GameEngineRegistrationCount != expectedEngineCount)
            {
                if (isLibrary)
                {
                    logger.LogError(
                        "Library plugin [{Name}] ({Type}) called AddGameEngine<T>() {Count} time(s); libraries must not register a game engine. " +
                        "Drop the AddGameEngine call or change the manifest 'kind' to 'game'.",
                        manifest.Name,
                        plugin.Module.GetType().FullName,
                        registration.GameEngineRegistrationCount);
                }
                else
                {
                    logger.LogError(
                        "Plugin [{Name}] ({Type}) called AddGameEngine<T>() {Count} time(s); exactly one call is required. " +
                        "The plugin will appear on the home page but will not be reachable at its route.",
                        manifest.Name,
                        plugin.Module.GetType().FullName,
                        registration.GameEngineRegistrationCount);
                }
            }
        }
    }
}
