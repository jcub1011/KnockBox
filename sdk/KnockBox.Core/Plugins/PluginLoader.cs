using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Result of discovering game plugins from a directory. Returned by
    /// <see cref="PluginLoader.LoadModules(string)"/> and consumed by the
    /// platform's DI registration code to wire every plugin's services and to
    /// expose the set of plugin assemblies to Blazor's router.
    /// </summary>
    /// <param name="Modules">Every discovered <see cref="IGameModule"/>, in discovery order.</param>
    /// <param name="Assemblies">The distinct plugin assemblies that contributed at least one module.</param>
    public sealed record PluginLoadResult(
        IReadOnlyList<IGameModule> Modules,
        IReadOnlyList<Assembly> Assemblies)
    {
        /// <summary>An empty result with no modules and no assemblies.</summary>
        public static PluginLoadResult Empty { get; } =
            new([], []);
    }

    /// <summary>
    /// Discovers <see cref="IGameModule"/> implementations from DLLs in a plugins directory.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "Startup-only discovery path. Log volume is bounded by the number of plugins in games/; readability of structured discovery/error messages is more valuable than LoggerMessage cache wins.")]
    public sealed class PluginLoader(ILogger<PluginLoader> logger)
    {
        /// <summary>
        /// Package ids a plugin's <c>.deps.json</c> must NOT reference. Right now
        /// only <c>KnockBox.Platform</c> is forbidden: referencing it from a
        /// plugin drags the Platform's types into the plugin's ALC and breaks
        /// the type-identity invariant that keeps host-shared contracts working
        /// across the host/plugin boundary.
        /// </summary>
        internal static readonly FrozenSet<string> ForbiddenPluginDependencies =
            FrozenSet.ToFrozenSet(["KnockBox.Platform"], StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Scans <paramref name="pluginsDirectory"/> for plugin folders, loads
        /// each into its own <see cref="PluginLoadContext"/>, reflects for
        /// <see cref="IGameModule"/> implementations, activates them, and
        /// returns the aggregate result. Modules with duplicate route
        /// identifiers are rejected (first wins); types that fail to activate
        /// are skipped with an error log.
        /// </summary>
        /// <param name="pluginsDirectory">
        /// Directory containing one subfolder per plugin. Each subfolder's
        /// primary DLL is expected to be named after the subfolder.
        /// </param>
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
            // Each plugin lives in its own subfolder (games/{TargetName}/) and publishes
            // its primary assembly as {TargetName}.dll alongside its transitive deps.
            // Each plugin gets its own PluginLoadContext so its transitive deps resolve
            // from its own folder via AssemblyDependencyResolver ({PluginName}.deps.json),
            // isolating version conflicts between plugins. Assemblies already loaded by
            // the host (shared contracts like KnockBox.Core, logging/DI abstractions,
            // BCL) are deferred to the default ALC so type identity is preserved across
            // the host/plugin boundary. Loose DLLs dropped directly under the plugins
            // root are ignored; the per-subdirectory layout is the only supported shape.
            var pluginDllPaths = new List<string>();
            foreach (var subdir in Directory.GetDirectories(pluginsDirectory))
            {
                var expected = Path.Combine(subdir, Path.GetFileName(subdir) + ".dll");
                if (File.Exists(expected))
                {
                    pluginDllPaths.Add(expected);
                }
                else
                {
                    logger.LogWarning(
                        "Plugin subdirectory [{Subdirectory}] is missing expected primary assembly [{ExpectedDll}]; skipping.",
                        subdir,
                        Path.GetFileName(expected));
                }
            }

            // Snapshot host-loaded assembly names ONCE, before any plugin loads. Using a
            // frozen snapshot makes IsSharedContract deterministic: every plugin sees the
            // same contract surface regardless of load order, and we avoid an O(N)
            // AppDomain scan on every assembly resolution. Anything loaded into the
            // default ALC *after* this point (including by earlier plugins) will be
            // treated as plugin-private by later plugins -- which is exactly what we
            // want for isolation.
            var hostAssemblyNames = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name)
                    .Where(n => n is not null)!,
                StringComparer.OrdinalIgnoreCase);

            var assemblies = new List<Assembly>(pluginDllPaths.Count);

            foreach (var dllPath in pluginDllPaths)
            {
                try
                {
                    var primaryAssemblyName = AssemblyName.GetAssemblyName(dllPath).Name;
                    if (string.IsNullOrEmpty(primaryAssemblyName))
                    {
                        logger.LogWarning(
                            "Plugin assembly at [{DllPath}] has no readable AssemblyName; skipping.",
                            dllPath);
                        continue;
                    }

                    var forbidden = FindForbiddenDependency(dllPath);
                    if (forbidden is not null)
                    {
                        logger.LogError(
                            "Plugin [{Assembly}] declares a dependency on [{ForbiddenPackage}] in its .deps.json. " +
                            "Plugins MUST reference only KnockBox.Core — referencing the Platform package breaks " +
                            "AssemblyLoadContext isolation and causes type-identity drift at runtime. " +
                            "Skipping this plugin.",
                            primaryAssemblyName,
                            forbidden);
                        continue;
                    }

                    bool IsSharedContract(AssemblyName name)
                    {
                        if (string.IsNullOrEmpty(name.Name))
                            return false;
                        // Never share the plugin's own primary assembly -- each plugin
                        // must load into its own ALC even if something with the same
                        // name is already in the default ALC.
                        if (string.Equals(name.Name, primaryAssemblyName, StringComparison.OrdinalIgnoreCase))
                            return false;
                        // Host-owned contracts (KnockBox.Core, logging/DI abstractions,
                        // BCL) must share type identity across the host/plugin boundary.
                        return hostAssemblyNames.Contains(name.Name);
                    }

                    var alc = new PluginLoadContext(dllPath, IsSharedContract);
                    var assembly = alc.LoadFromAssemblyPath(dllPath);
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
                // Fail the whole assembly on partial-load failures. Partial activation
                // of a broken plugin is worse than skipping it entirely: it leaves
                // ops with a confusing mix of logged errors and seemingly-working
                // modules that will blow up later when missing types are touched.
                foreach (var loaderException in ex.LoaderExceptions.Where(e => e is not null))
                {
                    logger.LogError(
                        loaderException,
                        "Loader exception while scanning [{Assembly}] for game modules; skipping the entire assembly.",
                        assembly.GetName().Name);
                }
                yield break;
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

        /// <summary>
        /// Scans the plugin's co-located <c>.deps.json</c> for any package id in
        /// <see cref="ForbiddenPluginDependencies"/>. Returns the offending id or
        /// <c>null</c> if nothing is found. A missing, unreadable, or malformed
        /// <c>.deps.json</c> all skip the check (return <c>null</c>) — the guard
        /// cannot inspect what it cannot parse, and the subsequent assembly load
        /// will surface any real IO problems with a clearer per-plugin error.
        /// </summary>
        internal static string? FindForbiddenDependency(string pluginDllPath)
        {
            var depsJsonPath = Path.ChangeExtension(pluginDllPath, ".deps.json");
            if (!File.Exists(depsJsonPath))
                return null;

            try
            {
                using var stream = File.OpenRead(depsJsonPath);
                using var doc = JsonDocument.Parse(stream);

                // The shape of deps.json is `{ "libraries": { "Name/Version": { ... } }, ... }`.
                // Walking `libraries` gives us every transitive package id; that's the
                // simplest surface to match ForbiddenPluginDependencies against.
                if (!doc.RootElement.TryGetProperty("libraries", out var libraries) ||
                    libraries.ValueKind != JsonValueKind.Object)
                    return null;

                foreach (var library in libraries.EnumerateObject())
                {
                    // Entries are keyed "{Id}/{Version}"; take the id half.
                    var slashIndex = library.Name.IndexOf('/');
                    var id = slashIndex > 0 ? library.Name[..slashIndex] : library.Name;

                    if (ForbiddenPluginDependencies.TryGetValue(id, out var forbidden))
                        return forbidden;
                }

                return null;
            }
            catch (Exception)
            {
                // IO failure (racy delete, permissions) or malformed JSON: we
                // can't enforce the guard here. The assembly load that follows
                // will fail with its own clearer error if the plugin is truly
                // broken; a well-formed plugin with a quirky deps.json simply
                // skips the forbidden-dep check.
                return null;
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
