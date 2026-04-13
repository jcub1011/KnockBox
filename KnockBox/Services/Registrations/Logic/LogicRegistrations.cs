using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Plugins;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services, PluginLoadResult pluginLoadResult)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            foreach (var module in pluginLoadResult.Modules)
            {
                module.RegisterServices(services);
                services.AddSingleton(typeof(IGameModule), module);
            }

            services.AddSingleton(new GamePluginAssemblies(pluginLoadResult.Assemblies));

            return services;
        }
    }
}
