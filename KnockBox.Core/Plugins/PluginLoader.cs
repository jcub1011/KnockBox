using System.Reflection;
using System.Runtime.Loader;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Result of discovering game plugins from a directory.
    /// </summary>
    public sealed record PluginLoadResult(
        IReadOnlyList<IGameModule> Modules,
        IReadOnlyList<Assembly> Assemblies)
    {
        public static PluginLoadResult Empty { get; } =
            new([], []);
    }

    /// <summary>
    /// Discovers <see cref="IGameModule"/> implementations from DLLs in a plugins directory.
    /// </summary>
    public sealed class PluginLoader(ILogger<PluginLoader> logger)
    {
        public PluginLoadResult LoadModules(string pluginsDirectory)
        {
            if (!Directory.Exists(pluginsDirectory))
            {
                logger.LogWarning(
                    "Plugins directory [{PluginsDirectory}] does not exist; no game modules will be loaded.",
                    pluginsDirectory);
                return PluginLoadResult.Empty;
            }

            var loadedAssemblies = LoadAssemblies(pluginsDirectory);
            var modules = new List<IGameModule>();
            var moduleAssemblies = new HashSet<Assembly>();
            var routeIdentifiers = new Dictionary<string, IGameModule>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in loadedAssemblies)
            {
                foreach (var moduleType in GetModuleTypes(assembly))
                {
                    var module = TryActivate(moduleType);
                    if (module is null)
                        continue;

                    if (routeIdentifiers.TryGetValue(module.RouteIdentifier, out var existing))
                    {
                        logger.LogError(
                            "Duplicate game module route identifier [{RouteIdentifier}]. " +
                            "Keeping [{ExistingType}] from [{ExistingAssembly}]; skipping [{SkippedType}] from [{SkippedAssembly}].",
                            module.RouteIdentifier,
                            existing.GetType().FullName,
                            existing.GetType().Assembly.GetName().Name,
                            moduleType.FullName,
                            moduleType.Assembly.GetName().Name);
                        continue;
                    }

                    routeIdentifiers.Add(module.RouteIdentifier, module);
                    modules.Add(module);
                    moduleAssemblies.Add(moduleType.Assembly);

                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation(
                            "Loaded game module [{Name}] with route identifier [{RouteIdentifier}] from [{Assembly}].",
                            module.Name,
                            module.RouteIdentifier,
                            moduleType.Assembly.GetName().Name);
                }
            }

            return new PluginLoadResult(modules, [.. moduleAssemblies]);
        }

        private List<Assembly> LoadAssemblies(string pluginsDirectory)
        {
            var dllPaths = Directory.GetFiles(pluginsDirectory, "*.dll", SearchOption.AllDirectories);
            var assemblies = new List<Assembly>(dllPaths.Length);

            foreach (var dllPath in dllPaths)
            {
                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                    var existing = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

                    var assembly = existing
                        ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);

                    assemblies.Add(assembly);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to load plugin assembly [{DllPath}].",
                        dllPath);
                }
            }

            return assemblies;
        }

        private IEnumerable<Type> GetModuleTypes(Assembly assembly)
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (var loaderException in ex.LoaderExceptions.Where(e => e is not null))
                {
                    logger.LogError(
                        loaderException,
                        "Loader exception while scanning [{Assembly}] for game modules.",
                        assembly.GetName().Name);
                }
                types = ex.Types;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to scan [{Assembly}] for game modules.",
                    assembly.GetName().Name);
                yield break;
            }

            foreach (var type in types)
            {
                if (type is null)
                    continue;
                if (type.IsInterface || type.IsAbstract)
                    continue;
                if (!typeof(IGameModule).IsAssignableFrom(type))
                    continue;

                yield return type;
            }
        }

        private IGameModule? TryActivate(Type moduleType)
        {
            try
            {
                if (Activator.CreateInstance(moduleType) is IGameModule module)
                    return module;

                logger.LogError(
                    "Type [{Type}] implements IGameModule but could not be activated as one.",
                    moduleType.FullName);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to activate game module [{Type}] from [{Assembly}]. " +
                    "Ensure it has a public parameterless constructor.",
                    moduleType.FullName,
                    moduleType.Assembly.GetName().Name);
                return null;
            }
        }
    }
}
