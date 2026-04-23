using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Result of discovering game plugins from a directory. Returned by
    /// <see cref="PluginLoader.LoadModules(string)"/> and consumed by the
    /// platform's DI registration code to wire every plugin's services and to
    /// expose the set of plugin assemblies to Blazor's router.
    /// </summary>
    /// <param name="Plugins">Every discovered plugin, in discovery order.</param>
    /// <param name="Assemblies">The distinct plugin assemblies that contributed at least one module.</param>
    public sealed record PluginLoadResult(
        IReadOnlyList<LoadedPlugin> Plugins,
        IReadOnlyList<Assembly> Assemblies)
    {
        /// <summary>An empty result with no plugins and no assemblies.</summary>
        public static PluginLoadResult Empty { get; } = new([], []);
    }

    /// <summary>
    /// Discovers plugin folders, validates each one's <c>plugin.json</c>,
    /// loads the plugin assembly into its own
    /// <see cref="AssemblyLoadContext"/>, activates the single
    /// <see cref="IGameModule"/> implementation inside, and returns the
    /// successfully loaded plugins.
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
        /// Scans <paramref name="pluginsDirectory"/> for plugin folders. For
        /// each subdirectory, reads <c>plugin.json</c>, locates
        /// <c>{EntryAssembly}.dll</c>, loads it into a per-plugin ALC, reflects
        /// for an <see cref="IGameModule"/>, cross-checks the module's reported
        /// manifest against the on-disk one, and accumulates the results.
        /// Duplicate route identifiers (first wins) and any validation failure
        /// skip the offending plugin with a logged error.
        /// </summary>
        public PluginLoadResult LoadModules(string pluginsDirectory)
        {
            if (!Directory.Exists(pluginsDirectory))
            {
                logger.LogWarning(
                    "Plugins directory [{PluginsDirectory}] does not exist; no game plugins will be loaded.",
                    pluginsDirectory);
                return PluginLoadResult.Empty;
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

            var plugins = new List<LoadedPlugin>();
            var assemblies = new HashSet<Assembly>();
            var routeIdentifiers = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);

            foreach (var subdir in Directory.GetDirectories(pluginsDirectory))
            {
                var loaded = TryLoadPluginFolder(subdir, hostAssemblyNames);
                if (loaded is null)
                    continue;

                if (routeIdentifiers.TryGetValue(loaded.Manifest.RouteIdentifier, out var existing))
                {
                    logger.LogError(
                        "Duplicate plugin route identifier [{RouteIdentifier}]. " +
                        "Keeping [{ExistingAssembly}]; skipping [{SkippedAssembly}].",
                        loaded.Manifest.RouteIdentifier,
                        existing.Assembly.GetName().Name,
                        loaded.Assembly.GetName().Name);
                    continue;
                }

                routeIdentifiers.Add(loaded.Manifest.RouteIdentifier, loaded);
                plugins.Add(loaded);
                assemblies.Add(loaded.Assembly);

                logger.LogInformation(
                    "Loaded game plugin [{Name}] with route identifier [{RouteIdentifier}] from [{Assembly}] v{Version}.",
                    loaded.Manifest.Name,
                    loaded.Manifest.RouteIdentifier,
                    loaded.Assembly.GetName().Name,
                    loaded.Manifest.Version);
            }

            return new PluginLoadResult(plugins, [.. assemblies]);
        }

        private LoadedPlugin? TryLoadPluginFolder(string subdir, HashSet<string> hostAssemblyNames)
        {
            var manifestPath = Path.Combine(subdir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                logger.LogError(
                    "Plugin folder [{Subdirectory}] is missing required [plugin.json]; skipping.",
                    subdir);
                return null;
            }

            var manifestResult = PluginManifest.TryReadFromFile(manifestPath);
            if (!manifestResult.TryGetSuccess(out var manifest))
            {
                manifestResult.TryGetFailure(out var error);
                logger.LogError(
                    "Plugin folder [{Subdirectory}] has an invalid plugin.json: {Error}; skipping.",
                    subdir,
                    error.PublicMessage);
                return null;
            }

            var dllPath = Path.Combine(subdir, manifest.EntryAssembly + ".dll");
            if (!File.Exists(dllPath))
            {
                logger.LogError(
                    "Plugin folder [{Subdirectory}] declares entryAssembly [{EntryAssembly}] but [{DllPath}] does not exist; skipping.",
                    subdir,
                    manifest.EntryAssembly,
                    dllPath);
                return null;
            }

            var depsInspection = InspectDepsJson(dllPath);
            if (depsInspection.ForbiddenDependency is not null)
            {
                logger.LogError(
                    "Plugin [{Assembly}] declares a dependency on [{ForbiddenPackage}] in its .deps.json. " +
                    "Plugins MUST reference only KnockBox.Core — referencing the Platform package breaks " +
                    "AssemblyLoadContext isolation and causes type-identity drift at runtime. Skipping.",
                    manifest.EntryAssembly,
                    depsInspection.ForbiddenDependency);
                return null;
            }

            var hostCoreVersion = ResolveHostCoreVersion();
            if (depsInspection.CoreVersion is { } pluginCoreVersion
                && hostCoreVersion is not null
                && pluginCoreVersion > hostCoreVersion)
            {
                logger.LogError(
                    "Plugin [{Assembly}] was compiled against KnockBox.Core v{PluginCore} but the host ships v{HostCore}; " +
                    "plugin would crash on unknown API calls at runtime. Skipping.",
                    manifest.EntryAssembly,
                    pluginCoreVersion,
                    hostCoreVersion);
                return null;
            }

            Assembly assembly;
            AssemblyLoadContext loadContext;
            try
            {
                bool IsSharedContract(AssemblyName name)
                {
                    if (string.IsNullOrEmpty(name.Name))
                        return false;
                    if (string.Equals(name.Name, manifest.EntryAssembly, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return hostAssemblyNames.Contains(name.Name);
                }

                loadContext = new PluginLoadContext(dllPath, IsSharedContract);
                assembly = loadContext.LoadFromAssemblyPath(dllPath);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to load plugin assembly [{DllPath}]; skipping.",
                    dllPath);
                return null;
            }

            var module = FindMatchingModule(assembly, manifest);
            if (module is null)
                return null;

            if (!ManifestsAgree(manifest, module.Manifest, out var disagreement))
            {
                logger.LogError(
                    "Plugin [{Assembly}]: on-disk plugin.json disagrees with IGameModule.Manifest — {Disagreement}. Skipping.",
                    assembly.GetName().Name,
                    disagreement);
                return null;
            }

            return new LoadedPlugin(module, manifest, assembly, loadContext);
        }

        /// <summary>
        /// Activates every <see cref="IGameModule"/> in <paramref name="assembly"/>
        /// and returns the single one whose
        /// <see cref="IPluginManifest.RouteIdentifier"/> matches the on-disk
        /// manifest. Modules whose ctors throw are logged and skipped; having a
        /// broken sibling module does not prevent the matching one from loading.
        /// </summary>
        private IGameModule? FindMatchingModule(Assembly assembly, IPluginManifest manifest)
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
                        "Loader exception while scanning [{Assembly}] for game modules; skipping the entire assembly.",
                        assembly.GetName().Name);
                }
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to scan [{Assembly}] for game modules.",
                    assembly.GetName().Name);
                return null;
            }

            IGameModule? match = null;
            foreach (var type in types)
            {
                if (type is null)
                    continue;
                if (type.IsInterface || type.IsAbstract)
                    continue;
                if (!typeof(IGameModule).IsAssignableFrom(type))
                    continue;

                var module = TryActivate(type);
                if (module is null)
                    continue;

                if (!string.Equals(module.Manifest.RouteIdentifier, manifest.RouteIdentifier, StringComparison.Ordinal))
                    continue;

                if (match is not null)
                {
                    logger.LogError(
                        "Plugin assembly [{Assembly}] has multiple IGameModule types claiming route [{Route}] ([{First}], [{Second}]); skipping.",
                        assembly.GetName().Name,
                        manifest.RouteIdentifier,
                        match.GetType().FullName,
                        type.FullName);
                    return null;
                }

                match = module;
            }

            if (match is null)
            {
                logger.LogError(
                    "Plugin assembly [{Assembly}] has no IGameModule implementation whose Manifest.RouteIdentifier matches the on-disk plugin.json route [{Route}]; skipping.",
                    assembly.GetName().Name,
                    manifest.RouteIdentifier);
            }

            return match;
        }

        /// <summary>
        /// Result of scanning a plugin's co-located <c>.deps.json</c>.
        /// </summary>
        /// <param name="ForbiddenDependency">
        /// The first package id matching <see cref="ForbiddenPluginDependencies"/>, or <c>null</c> if none.
        /// </param>
        /// <param name="CoreVersion">
        /// Version the plugin declared against <c>KnockBox.Core</c>, or <c>null</c> if the
        /// deps.json was missing, unparsable, or did not reference <c>KnockBox.Core</c>.
        /// Used by the loader to reject plugins compiled against a newer Core than the
        /// host ships.
        /// </param>
        internal readonly record struct DepsInspection(string? ForbiddenDependency, Version? CoreVersion);

        /// <summary>
        /// Scans the plugin's co-located <c>.deps.json</c> for any package id in
        /// <see cref="ForbiddenPluginDependencies"/> and extracts the version the plugin
        /// declared against <c>KnockBox.Core</c>. A missing, unreadable, or malformed
        /// <c>.deps.json</c> returns an empty inspection — the guards cannot inspect
        /// what they cannot parse, and the subsequent assembly load will surface any
        /// real IO problems with a clearer per-plugin error. Parse failures are logged
        /// at warning level so a broken <c>.deps.json</c> doesn't silently bypass the
        /// forbidden-dependency check.
        /// </summary>
        internal DepsInspection InspectDepsJson(string pluginDllPath)
        {
            var depsJsonPath = Path.ChangeExtension(pluginDllPath, ".deps.json");
            if (!File.Exists(depsJsonPath))
                return default;

            try
            {
                using var stream = File.OpenRead(depsJsonPath);
                using var doc = JsonDocument.Parse(stream);

                if (!doc.RootElement.TryGetProperty("libraries", out var libraries) ||
                    libraries.ValueKind != JsonValueKind.Object)
                    return default;

                string? forbidden = null;
                Version? coreVersion = null;

                foreach (var library in libraries.EnumerateObject())
                {
                    var slashIndex = library.Name.IndexOf('/');
                    var id = slashIndex > 0 ? library.Name[..slashIndex] : library.Name;
                    var versionText = slashIndex > 0 && slashIndex + 1 < library.Name.Length
                        ? library.Name[(slashIndex + 1)..]
                        : null;

                    if (forbidden is null && ForbiddenPluginDependencies.TryGetValue(id, out var match))
                        forbidden = match;

                    if (coreVersion is null
                        && string.Equals(id, "KnockBox.Core", StringComparison.OrdinalIgnoreCase)
                        && versionText is not null
                        && Version.TryParse(StripPrereleaseSuffix(versionText), out var parsed))
                    {
                        coreVersion = parsed;
                    }

                    if (forbidden is not null && coreVersion is not null)
                        break;
                }

                return new DepsInspection(forbidden, coreVersion);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to parse .deps.json for plugin [{Dll}]; forbidden-dependency and Core-version checks will be skipped.",
                    pluginDllPath);
                return default;
            }
        }

        /// <summary>
        /// Drops an optional SemVer prerelease/build suffix (<c>-alpha</c>, <c>-rc.1</c>,
        /// <c>+abc123</c>) before handing the core numeric component to
        /// <see cref="Version.TryParse(string?, out Version?)"/>, which doesn't
        /// accept SemVer-style suffixes.
        /// </summary>
        private static string StripPrereleaseSuffix(string versionText)
        {
            int dash = versionText.IndexOf('-');
            int plus = versionText.IndexOf('+');
            int cut = (dash, plus) switch
            {
                (< 0, < 0) => -1,
                (< 0, var p) => p,
                (var d, < 0) => d,
                var (d, p) => Math.Min(d, p),
            };
            return cut < 0 ? versionText : versionText[..cut];
        }

        /// <summary>
        /// Resolves the host's <c>KnockBox.Core</c> version for plugin-compat
        /// gating. Prefers <see cref="AssemblyInformationalVersionAttribute"/>
        /// (carries the full SemVer driven by <c>-p:Version=…</c> at pack time)
        /// and falls back to <see cref="AssemblyName.Version"/>. Reading from
        /// InformationalVersion matters because the assembly's <c>Version</c>
        /// is pinned at the major for binary compatibility across v1.x, so
        /// using it would falsely reject any plugin built against a later
        /// minor.
        /// </summary>
        internal static Version? ResolveHostCoreVersion()
        {
            var assembly = typeof(IGameModule).Assembly;
            var infoVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(infoVersion)
                && Version.TryParse(StripPrereleaseSuffix(infoVersion), out var parsed))
            {
                return parsed;
            }

            return assembly.GetName().Version;
        }

        /// <summary>
        /// Max time a plugin's <see cref="IGameModule"/> constructor is allowed to run
        /// before the loader gives up and skips the module. A deadlocking ctor would
        /// otherwise hang host startup indefinitely.
        /// </summary>
        internal static readonly TimeSpan ModuleActivationTimeout = TimeSpan.FromSeconds(5);

        private IGameModule? TryActivate(Type moduleType)
        {
            try
            {
                // Module constructors run on the startup thread — a misbehaving plugin
                // can hang host boot by blocking in its ctor. Isolate the construction
                // on the thread pool with a hard timeout.
                var activation = Task.Run(() => Activator.CreateInstance(moduleType));
                if (!activation.Wait(ModuleActivationTimeout))
                {
                    logger.LogError(
                        "Game module [{Type}] from [{Assembly}] exceeded the {Timeout:g} activation timeout; skipping.",
                        moduleType.FullName,
                        moduleType.Assembly.GetName().Name,
                        ModuleActivationTimeout);
                    return null;
                }

                if (activation.Result is IGameModule module)
                    return module;

                logger.LogError(
                    "Type [{Type}] implements IGameModule but could not be activated as one.",
                    moduleType.FullName);
                return null;
            }
            catch (AggregateException agg) when (agg.InnerException is not null)
            {
                logger.LogError(
                    agg.InnerException,
                    "Failed to activate game module [{Type}] from [{Assembly}]. " +
                    "Ensure it has a public parameterless constructor.",
                    moduleType.FullName,
                    moduleType.Assembly.GetName().Name);
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

        /// <summary>
        /// Returns <c>true</c> if the on-disk manifest and the module's reported
        /// manifest agree on every identity and capability field. On mismatch
        /// sets <paramref name="disagreement"/> to a short human-readable reason.
        /// </summary>
        internal static bool ManifestsAgree(
            IPluginManifest onDisk,
            IPluginManifest fromModule,
            out string disagreement)
        {
            if (!string.Equals(onDisk.Name, fromModule.Name, StringComparison.Ordinal))
            { disagreement = $"Name disk=[{onDisk.Name}] code=[{fromModule.Name}]"; return false; }
            if (!string.Equals(onDisk.Description, fromModule.Description, StringComparison.Ordinal))
            { disagreement = $"Description disk=[{onDisk.Description}] code=[{fromModule.Description}]"; return false; }
            if (!string.Equals(onDisk.RouteIdentifier, fromModule.RouteIdentifier, StringComparison.Ordinal))
            { disagreement = $"RouteIdentifier disk=[{onDisk.RouteIdentifier}] code=[{fromModule.RouteIdentifier}]"; return false; }
            if (!string.Equals(onDisk.EntryAssembly, fromModule.EntryAssembly, StringComparison.Ordinal))
            { disagreement = $"EntryAssembly disk=[{onDisk.EntryAssembly}] code=[{fromModule.EntryAssembly}]"; return false; }
            if (onDisk.Version != fromModule.Version)
            { disagreement = $"Version disk=[{onDisk.Version}] code=[{fromModule.Version}]"; return false; }
            if (!onDisk.Capabilities.SetEquals(fromModule.Capabilities))
            {
                disagreement =
                    $"Capabilities disk=[{string.Join(',', onDisk.Capabilities)}] " +
                    $"code=[{string.Join(',', fromModule.Capabilities)}]";
                return false;
            }

            disagreement = string.Empty;
            return true;
        }
    }
}
