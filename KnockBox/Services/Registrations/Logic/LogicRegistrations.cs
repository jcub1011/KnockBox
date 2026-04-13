using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
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
            var pluginsPath = Path.Combine(AppContext.BaseDirectory, "games");
            if (Directory.Exists(pluginsPath))
            {
                var dllFiles = Directory.GetFiles(pluginsPath, "*.dll");
                foreach (var dll in dllFiles)
                {
                    try
                    {
                        var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(dll);
                        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == assemblyName.Name))
                        {
                            System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log or handle assembly loading errors
                        Console.WriteLine($"Error loading plugin assembly: {dll}. Exception: {ex.Message}");
                    }
                }
            }

            var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            var pluginAssemblies = new HashSet<System.Reflection.Assembly>();

            foreach (var moduleType in moduleTypes)
            {
                if (Activator.CreateInstance(moduleType) is IGameModule module)
                {
                    module.RegisterServices(services);
                    services.AddSingleton(typeof(IGameModule), module);
                    pluginAssemblies.Add(moduleType.Assembly);
                }
            }

            services.AddSingleton(new GamePluginAssemblies(pluginAssemblies));

            return services;
        }
    }
}
