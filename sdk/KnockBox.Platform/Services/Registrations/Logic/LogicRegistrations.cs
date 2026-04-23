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
        public static IServiceCollection RegisterLogic(this IServiceCollection services, PluginLoadResult pluginLoadResult, ILogger logger)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            foreach (var plugin in pluginLoadResult.Plugins)
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

                var registration = new DefaultPluginRegistration(services, manifest);

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
                    continue;
                }

                if (registration.GameEngineRegistrationCount != 1)
                {
                    logger.LogError(
                        "Plugin [{Name}] ({Type}) called AddGameEngine<T>() {Count} time(s); exactly one call is required. " +
                        "The plugin will appear on the home page but will not be reachable at its route.",
                        manifest.Name,
                        plugin.Module.GetType().FullName,
                        registration.GameEngineRegistrationCount);
                }

                // The module is still exposed as IGameModule so the home page
                // and admin dashboard can enumerate it even if the engine
                // registration was malformed.
                services.AddSingleton(typeof(IGameModule), plugin.Module);
            }

            services.AddSingleton(new GamePluginAssemblies(pluginLoadResult.Assemblies));

            return services;
        }
    }
}
