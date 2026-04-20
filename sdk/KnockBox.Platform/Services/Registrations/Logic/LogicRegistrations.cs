using KnockBox.Platform.Filtering;
using KnockBox.Platform.Games;
using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Core.Plugins;
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

            foreach (var module in pluginLoadResult.Modules)
            {
                try
                {
                    module.RegisterServices(services);
                    services.AddSingleton(typeof(IGameModule), module);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to register services for game module [{Name}] ({Type}); skipping.",
                        module.Name,
                        module.GetType().FullName);
                }
            }

            services.AddSingleton(new GamePluginAssemblies(pluginLoadResult.Assemblies));

            return services;
        }
    }
}
