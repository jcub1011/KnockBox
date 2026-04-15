using KnockBox.Services.Logic.Admin;
using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components.Server.Circuits;
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

            // Admin / management surface. Registered here so LobbyService can
            // take a ctor dependency on IGameAvailabilityService and the
            // dashboard can see up-to-date metrics / log files.
            services.AddSingleton<IGameAvailabilityService, GameAvailabilityService>();
            services.AddSingleton<IAdminLogService, AdminLogService>();
            services.AddSingleton<IAdminMetricsService, AdminMetricsService>();
            services.AddScoped<CircuitHandler, AdminCircuitTracker>();

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
