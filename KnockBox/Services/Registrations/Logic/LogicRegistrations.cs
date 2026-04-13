using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Core.Plugins;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            // Dynamically discover and register game modules
            var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var moduleType in moduleTypes)
            {
                if (Activator.CreateInstance(moduleType) is IGameModule module)
                {
                    module.RegisterServices(services);
                    services.AddSingleton(module);
                }
            }

            return services;
        }
    }
}
