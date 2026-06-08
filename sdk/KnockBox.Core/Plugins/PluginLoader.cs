using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace KnockBox.Core.Plugins
{
    /// <summary>
    /// Result of discovering game and library plugins from one or more root
    /// directories. Returned by
    /// <see cref="PluginLoader.LoadModules(IReadOnlyList{string})"/> and consumed
    /// by the platform's DI registration code to wire every plugin's services
    /// and to expose the set of plugin assemblies to Blazor's router. Plugins
    /// are ordered library-first so the registration pipeline can register
    /// library services before any game tries to inject them.
    /// </summary>
    /// <param name="Plugins">Every discovered plugin, library-first.</param>
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
    /// <see cref="IPluginModule"/> implementation inside, and returns the
    /// successfully loaded plugins. Handles both <see cref="IGameModule"/> and
    /// <see cref="ILibraryModule"/> plugins; the manifest's
    /// <see cref="IPluginManifest.Kind"/> decides which type the loader expects
    /// to find inside the plugin assembly.
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
        /// Single-root convenience overload. Equivalent to calling
        /// <see cref="LoadModules(IReadOnlyList{string})"/> with a one-element list.
        /// Used by tests and by hosts that only have one plugin root.
        /// </summary>
        public PluginLoadResult LoadModules(string pluginsDirectory) =>
            LoadModules(new[] { pluginsDirectory });

        /// <summary>
        /// Scans every directory in <paramref name="rootPaths"/> for plugin folders.
        /// Each subdirectory must contain a <c>plugin.json</c>; the manifest's
        /// <see cref="IPluginManifest.Kind"/> determines whether the folder is loaded
        /// as a game or library plugin (the root folder is only a convention).
        /// </summary>
        /// <remarks>
        /// <para>The pipeline runs in three phases and enforces the
        /// "all libraries finish before any game starts" invariant:</para>
        /// <list type="number">
        ///   <item><b>Discovery + manifest read:</b> every subdir's <c>plugin.json</c>
        ///   is parsed; folders with missing or invalid manifests are skipped.</item>
        ///   <item><b>Library patch dedup + contract promotion:</b> library plugins
        ///   are partitioned by <c>(entryAssembly, Major, Minor)</c>; within each
        ///   group only the highest Patch survives. Each surviving library's
        ///   <see cref="IPluginManifest.ExportedContracts"/> DLLs are loaded into
        ///   the default <see cref="AssemblyLoadContext"/> before any plugin ALC is
        ///   constructed, so consumer plugins see identical CLR types for the
        ///   contract interfaces.</item>
        ///   <item><b>Shareable-dep promotion + module activation:</b> deps shipped
        ///   by 2+ plugins at compatible versions are promoted, then libraries are
        ///   activated first and games second. Game plugins consuming library
        ///   services are guaranteed those services are already DI-resolvable when
        ///   the host's registration pipeline reaches them.</item>
        /// </list>
        /// <para>Duplicate route identifiers across loaded plugins are first-wins;
        /// the duplicate is logged and skipped.</para>
        /// </remarks>
        public PluginLoadResult LoadModules(IReadOnlyList<string> rootPaths)
        {
            if (rootPaths.Count == 0)
                return PluginLoadResult.Empty;

            // Dedup subdirs by canonical full path so a misconfigured host whose
            // LibrariesPaths and PluginsPaths overlap (or whose roots are aliases
            // of the same directory) doesn't discover the same plugin twice and
            // promote its contracts twice. The first occurrence wins; later
            // duplicates are logged so the misconfig is visible.
            var allSubdirs = new List<string>();
            var seenSubdirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in rootPaths)
            {
                if (!Directory.Exists(root))
                {
                    logger.LogWarning(
                        "Plugin root [{Root}] does not exist; no plugins will be loaded from it.",
                        root);
                    continue;
                }
                foreach (var subdir in Directory.GetDirectories(root))
                {
                    var canonical = Path.GetFullPath(subdir);
                    if (seenSubdirs.Add(canonical))
                    {
                        allSubdirs.Add(subdir);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Plugin subdirectory [{Subdir}] is reachable from multiple roots; only the first occurrence is loaded.",
                            canonical);
                    }
                }
            }

            if (allSubdirs.Count == 0)
            {
                logger.LogInformation(
                    "No plugin subfolders discovered across {Count} root path(s); returning empty load result.",
                    rootPaths.Count);
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

            // Frozen copy of the original snapshot. Distinguishes "the host shipped
            // this assembly" from "we promoted this assembly during contract promotion".
            // A library plugin attempting to export a contract whose simple name is in
            // originalHostAssemblies is hijacking a host-owned identity and is rejected.
            var originalHostAssemblies = new HashSet<string>(hostAssemblyNames, StringComparer.OrdinalIgnoreCase);

            // Read every manifest once. Folders with missing/invalid manifests are
            // dropped here; everything downstream operates on parsed manifests only.
            var discovered = new List<DiscoveredPlugin>();
            foreach (var subdir in allSubdirs)
            {
                var manifestPath = Path.Combine(subdir, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    logger.LogError(
                        "Plugin folder [{Subdirectory}] is missing required [plugin.json]; skipping.",
                        subdir);
                    continue;
                }

                var manifestResult = PluginManifest.TryReadFromFile(manifestPath);
                if (!manifestResult.TryGetSuccess(out var manifest))
                {
                    manifestResult.TryGetFailure(out var error);
                    logger.LogError(
                        "Plugin folder [{Subdirectory}] has an invalid plugin.json: {Error}; skipping.",
                        subdir,
                        error.PublicMessage);
                    continue;
                }

                discovered.Add(new DiscoveredPlugin(subdir, manifest));
            }

            // Partition by kind. Libraries are processed first, then games.
            var libraries = discovered.Where(d => d.Manifest.Kind == PluginKind.Library).ToList();
            var games = discovered.Where(d => d.Manifest.Kind == PluginKind.Game).ToList();

            // Library patch dedup: within each (entryAssembly, Major, Minor) group,
            // keep the highest Patch.
            var dedupedLibraries = DedupLibrariesByPatch(libraries);

            // Promote each surviving library's exportedContracts DLLs into the default
            // ALC. Libraries whose contracts are missing or collide with host-owned
            // identities are dropped here.
            var promotedLibraries = PromoteExportedContracts(
                dedupedLibraries,
                originalHostAssemblies,
                hostAssemblyNames);

            // Existing multi-shipper shareable-dep promotion. Operates on the subdirs
            // of every plugin (library + game) that survived prior validation.
            var allSurvivingSubdirs = promotedLibraries
                .Concat(games)
                .Select(d => d.Subdir)
                .ToArray();
            PromoteShareableDependencies(allSurvivingSubdirs, hostAssemblyNames);

            // Library-first activation. The two passes share their state so that a
            // game with a duplicate route to a library (vanishingly unlikely but
            // possible if someone misconfigures kind/route) gets flagged.
            var plugins = new List<LoadedPlugin>();
            var assemblies = new HashSet<Assembly>();
            var routeIdentifiers = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);

            foreach (var discovered_ in promotedLibraries)
                TryAcceptPlugin(discovered_, hostAssemblyNames, plugins, assemblies, routeIdentifiers);

            foreach (var discovered_ in games)
                TryAcceptPlugin(discovered_, hostAssemblyNames, plugins, assemblies, routeIdentifiers);

            return new PluginLoadResult(plugins, [.. assemblies]);
        }

        /// <summary>
        /// One step of the inner load loop: load the assembly for a pre-validated
        /// manifest, find the matching module, and append to <paramref name="plugins"/>
        /// (skipping duplicates by route identifier). Extracted from the loop to keep
        /// <see cref="LoadModules(IReadOnlyList{string})"/> readable.
        /// </summary>
        private void TryAcceptPlugin(
            DiscoveredPlugin discovered,
            HashSet<string> hostAssemblyNames,
            List<LoadedPlugin> plugins,
            HashSet<Assembly> assemblies,
            Dictionary<string, LoadedPlugin> routeIdentifiers)
        {
            var loaded = TryLoadPluginFolder(discovered.Subdir, discovered.Manifest, hostAssemblyNames);
            if (loaded is null)
                return;

            if (routeIdentifiers.TryGetValue(loaded.Manifest.RouteIdentifier, out var existing))
            {
                logger.LogError(
                    "Duplicate plugin route identifier [{RouteIdentifier}]. " +
                    "Keeping [{ExistingAssembly}]; skipping [{SkippedAssembly}].",
                    loaded.Manifest.RouteIdentifier,
                    existing.Assembly.GetName().Name,
                    loaded.Assembly.GetName().Name);
                return;
            }

            routeIdentifiers.Add(loaded.Manifest.RouteIdentifier, loaded);
            plugins.Add(loaded);
            assemblies.Add(loaded.Assembly);

            logger.LogInformation(
                "Loaded {Kind} plugin [{Name}] with route identifier [{RouteIdentifier}] from [{Assembly}] v{Version}.",
                loaded.Manifest.Kind.ToString().ToLowerInvariant(),
                loaded.Manifest.Name,
                loaded.Manifest.RouteIdentifier,
                loaded.Assembly.GetName().Name,
                loaded.Manifest.Version);
        }

        /// <summary>
        /// A plugin folder whose <c>plugin.json</c> has been successfully parsed.
        /// Pairs the subdir path with the parsed manifest so downstream phases
        /// (dedup, contract promotion, activation) don't re-parse the same file.
        /// </summary>
        internal readonly record struct DiscoveredPlugin(string Subdir, IPluginManifest Manifest);

        /// <summary>
        /// Partitions library plugins by <c>(entryAssembly, Major, Minor)</c> and
        /// returns the highest-<c>Patch</c> survivor per group. Different Major or
        /// Minor lands in different groups, so both side-by-side versions survive.
        /// Drops are logged at Information level — they're not errors, just an
        /// older patch being superseded by a newer one.
        /// </summary>
        internal IReadOnlyList<DiscoveredPlugin> DedupLibrariesByPatch(
            IReadOnlyList<DiscoveredPlugin> libraries)
        {
            // Group by (entryAssembly OrdinalIgnoreCase, Major, Minor). Within each
            // group, keep the entry whose manifest version has the highest Build (Patch).
            var groups = new Dictionary<(string Entry, int Major, int Minor),
                List<DiscoveredPlugin>>(
                comparer: new LibraryGroupKeyComparer());

            foreach (var library in libraries)
            {
                var v = library.Manifest.Version;
                var key = (library.Manifest.EntryAssembly, v.Major, v.Minor);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<DiscoveredPlugin>();
                list.Add(library);
            }

            var survivors = new List<DiscoveredPlugin>(libraries.Count);
            foreach (var (_, group) in groups)
            {
                if (group.Count == 1)
                {
                    survivors.Add(group[0]);
                    continue;
                }

                var winner = group[0];
                for (int i = 1; i < group.Count; i++)
                {
                    if (group[i].Manifest.Version > winner.Manifest.Version)
                        winner = group[i];
                }

                foreach (var entry in group)
                {
                    if (ReferenceEquals(entry.Subdir, winner.Subdir))
                        continue;
                    logger.LogInformation(
                        "Library plugin [{Loser}] v{LoserVersion} at [{LoserPath}] superseded by [{Winner}] v{WinnerVersion} at [{WinnerPath}] " +
                        "(same Major.Minor, lower Patch).",
                        entry.Manifest.EntryAssembly,
                        entry.Manifest.Version,
                        entry.Subdir,
                        winner.Manifest.EntryAssembly,
                        winner.Manifest.Version,
                        winner.Subdir);
                }

                survivors.Add(winner);
            }

            return survivors;
        }

        private sealed class LibraryGroupKeyComparer
            : IEqualityComparer<(string Entry, int Major, int Minor)>
        {
            public bool Equals(
                (string Entry, int Major, int Minor) x,
                (string Entry, int Major, int Minor) y) =>
                string.Equals(x.Entry, y.Entry, StringComparison.OrdinalIgnoreCase)
                && x.Major == y.Major
                && x.Minor == y.Minor;

            public int GetHashCode((string Entry, int Major, int Minor) obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Entry),
                    obj.Major,
                    obj.Minor);
        }

        /// <summary>
        /// Loads every surviving library's exportedContracts DLLs into the default
        /// <see cref="AssemblyLoadContext"/>, in the order libraries appear in
        /// <paramref name="libraries"/>. Maintains the load-time invariant that
        /// every consumer plugin sees an identical CLR type for the exported
        /// contract interfaces, regardless of which ALC the consumer lives in.
        /// </summary>
        /// <remarks>
        /// Drops a library plugin entirely (returns it omitted from the result)
        /// when any of:
        /// <list type="bullet">
        ///   <item>A declared contract DLL is missing from the plugin folder.</item>
        ///   <item>A declared contract's simple name collides with a host-shipped
        ///   assembly — the host owns that identity and a plugin must not redefine it.</item>
        ///   <item>The contract assembly's full identity (name + version + token)
        ///   differs from a previously-promoted assembly with the same simple name
        ///   but a different full identity that the host considers a conflict.
        ///   (Same simple name with different version IS allowed — that's the
        ///   side-by-side coexistence case.)</item>
        /// </list>
        /// <para>If two libraries declare contracts whose full identities are
        /// exactly equal, the contract is promoted once and both libraries reuse
        /// it (e.g. v1.2 and v1.3 of the same library shipping the same contracts
        /// because the contract surface didn't change between minors).</para>
        /// <para><b>Side effect:</b> <paramref name="promotedAssemblyNames"/> is
        /// mutated — every successfully-promoted contract's simple name is added
        /// so downstream phases (shareable-dep promotion, ALC isolation) treat
        /// it as host-loaded. Pass a set you're prepared to see grow.</para>
        /// </remarks>
        internal IReadOnlyList<DiscoveredPlugin> PromoteExportedContracts(
            IReadOnlyList<DiscoveredPlugin> libraries,
            HashSet<string> originalHostAssemblies,
            HashSet<string> promotedAssemblyNames)
        {
            var survivors = new List<DiscoveredPlugin>(libraries.Count);

            // Track every contract identity we have promoted. Key = full identity
            // (simpleName + version + token). Map value is just a marker.
            var promotedByIdentity = new HashSet<(string Name, string Version, string Token)>(
                new ContractIdentityComparer());

            foreach (var library in libraries)
            {
                if (library.Manifest.ExportedContracts.Count == 0)
                {
                    // Library that exports no contracts is fine — it can still register
                    // services keyed by types defined in KnockBox.Core. Just accept it.
                    survivors.Add(library);
                    continue;
                }

                bool libraryRejected = false;

                foreach (var simpleName in library.Manifest.ExportedContracts)
                {
                    var dllPath = Path.Combine(library.Subdir, simpleName + ".dll");
                    if (!File.Exists(dllPath))
                    {
                        logger.LogError(
                            "Library plugin [{Library}] declares exportedContract [{Contract}] but [{Path}] does not exist; skipping the library.",
                            library.Manifest.EntryAssembly,
                            simpleName,
                            dllPath);
                        libraryRejected = true;
                        break;
                    }

                    if (originalHostAssemblies.Contains(simpleName))
                    {
                        logger.LogError(
                            "Library plugin [{Library}] tries to export contract [{Contract}] but the host already ships an assembly by that simple name; " +
                            "promoting it would shadow the host. Skipping the library.",
                            library.Manifest.EntryAssembly,
                            simpleName);
                        libraryRejected = true;
                        break;
                    }

                    AssemblyName contractIdentity;
                    try
                    {
                        contractIdentity = AssemblyName.GetAssemblyName(dllPath);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Library plugin [{Library}]: failed to read AssemblyName from contract [{Path}]; skipping the library.",
                            library.Manifest.EntryAssembly,
                            dllPath);
                        libraryRejected = true;
                        break;
                    }

                    var version = contractIdentity.Version?.ToString() ?? "0.0.0.0";
                    var token = contractIdentity.GetPublicKeyToken() is { Length: > 0 } t
                        ? Convert.ToHexString(t)
                        : string.Empty;
                    var identity = (simpleName, version, token);

                    if (promotedByIdentity.Contains(identity))
                    {
                        // Same simple name + version + token: a sibling library has
                        // already promoted this exact assembly. Reuse it; no-op.
                        continue;
                    }

                    try
                    {
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Library plugin [{Library}]: failed to promote contract [{Path}] into the default ALC; skipping the library.",
                            library.Manifest.EntryAssembly,
                            dllPath);
                        libraryRejected = true;
                        break;
                    }

                    promotedByIdentity.Add(identity);
                    promotedAssemblyNames.Add(simpleName);

                    logger.LogInformation(
                        "Promoted library contract [{Contract}] v{Version} into the default ALC for library plugin [{Library}].",
                        simpleName,
                        version,
                        library.Manifest.EntryAssembly);
                }

                if (!libraryRejected)
                    survivors.Add(library);
            }

            return survivors;
        }

        private sealed class ContractIdentityComparer
            : IEqualityComparer<(string Name, string Version, string Token)>
        {
            public bool Equals(
                (string Name, string Version, string Token) x,
                (string Name, string Version, string Token) y) =>
                string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Version, y.Version, StringComparison.Ordinal)
                && string.Equals(x.Token, y.Token, StringComparison.Ordinal);

            public int GetHashCode((string Name, string Version, string Token) obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                    obj.Version,
                    obj.Token);
        }

        private LoadedPlugin? TryLoadPluginFolder(string subdir, IPluginManifest manifest, HashSet<string> hostAssemblyNames)
        {
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
        /// Activates every <see cref="IPluginModule"/> in <paramref name="assembly"/>
        /// and returns the single one whose
        /// <see cref="IPluginManifest.RouteIdentifier"/> matches the on-disk
        /// manifest and whose runtime type matches
        /// <see cref="IPluginManifest.Kind"/>. Modules whose ctors throw are
        /// logged and skipped; having a broken sibling module does not prevent
        /// the matching one from loading.
        /// </summary>
        /// <remarks>
        /// The kind/type cross-check is the second of two enforcement layers:
        /// the parser already rejects e.g. a manifest with
        /// <c>kind: library</c> + <c>exportedContracts: []</c> as ambiguous, but
        /// the parser cannot know what interface the module type implements.
        /// This method catches the case where a manifest says "library" but the
        /// type implements <see cref="IGameModule"/> (or vice-versa).
        /// </remarks>
        private IPluginModule? FindMatchingModule(Assembly assembly, IPluginManifest manifest)
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
                        "Loader exception while scanning [{Assembly}] for plugin modules; skipping the entire assembly.",
                        assembly.GetName().Name);
                }
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to scan [{Assembly}] for plugin modules.",
                    assembly.GetName().Name);
                return null;
            }

            // The required runtime interface depends on the manifest's kind.
            // Game manifests must resolve to IGameModule; library manifests must
            // resolve to ILibraryModule. We scan for the broader IPluginModule
            // surface and then enforce the exact-kind rule below so we can emit
            // a precise error if e.g. a "library" manifest's type implements
            // IGameModule instead.
            var requiredType = manifest.Kind switch
            {
                PluginKind.Library => typeof(ILibraryModule),
                _ => typeof(IGameModule),
            };

            IPluginModule? match = null;
            foreach (var type in types)
            {
                if (type is null)
                    continue;
                if (type.IsInterface || type.IsAbstract)
                    continue;
                if (!typeof(IPluginModule).IsAssignableFrom(type))
                    continue;

                var module = TryActivate(type);
                if (module is null)
                    continue;

                if (!string.Equals(module.Manifest.RouteIdentifier, manifest.RouteIdentifier, StringComparison.Ordinal))
                    continue;

                if (!requiredType.IsInstanceOfType(module))
                {
                    logger.LogError(
                        "Plugin assembly [{Assembly}] declares kind [{Kind}] in its manifest but module type [{Type}] does not implement [{Required}]; skipping.",
                        assembly.GetName().Name,
                        manifest.Kind,
                        type.FullName,
                        requiredType.Name);
                    return null;
                }

                if (match is not null)
                {
                    logger.LogError(
                        "Plugin assembly [{Assembly}] has multiple {Required} types claiming route [{Route}] ([{First}], [{Second}]); skipping.",
                        assembly.GetName().Name,
                        requiredType.Name,
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
                    "Plugin assembly [{Assembly}] has no {Required} implementation whose Manifest.RouteIdentifier matches the on-disk plugin.json route [{Route}]; skipping.",
                    assembly.GetName().Name,
                    requiredType.Name,
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
        internal readonly record struct DepsInspection(
            string? ForbiddenDependency,
            Version? CoreVersion,
            IReadOnlyList<PluginDependency> Dependencies)
        {
            public DepsInspection(string? ForbiddenDependency, Version? CoreVersion)
                : this(ForbiddenDependency, CoreVersion, Array.Empty<PluginDependency>()) { }
        }

        /// <summary>
        /// A single transitive dependency a plugin ships alongside its entry assembly,
        /// as discovered by scanning the plugin's <c>.deps.json</c> and verifying that
        /// a matching DLL exists in the plugin folder. Used by
        /// <see cref="PromoteShareableDependencies"/> to deduplicate copies of the
        /// same library across plugins that ship semver-compatible versions.
        /// </summary>
        /// <param name="SimpleName">Assembly simple name (no extension).</param>
        /// <param name="Version">Parsed version, prerelease/build suffix stripped.</param>
        /// <param name="DllPath">Absolute path to the dependency DLL in the plugin folder.</param>
        internal readonly record struct PluginDependency(string SimpleName, Version Version, string DllPath);

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
                return new DepsInspection(null, null);

            try
            {
                using var stream = File.OpenRead(depsJsonPath);
                using var doc = JsonDocument.Parse(stream);

                if (!doc.RootElement.TryGetProperty("libraries", out var libraries) ||
                    libraries.ValueKind != JsonValueKind.Object)
                    return new DepsInspection(null, null);

                string? forbidden = null;
                Version? coreVersion = null;
                var pluginDir = Path.GetDirectoryName(pluginDllPath) ?? string.Empty;
                List<PluginDependency>? dependencies = null;

                foreach (var library in libraries.EnumerateObject())
                {
                    var slashIndex = library.Name.IndexOf('/');
                    var id = slashIndex > 0 ? library.Name[..slashIndex] : library.Name;
                    var versionText = slashIndex > 0 && slashIndex + 1 < library.Name.Length
                        ? library.Name[(slashIndex + 1)..]
                        : null;

                    if (forbidden is null && ForbiddenPluginDependencies.TryGetValue(id, out var match))
                        forbidden = match;

                    if (versionText is not null
                        && Version.TryParse(StripPrereleaseSuffix(versionText), out var parsedVersion))
                    {
                        if (coreVersion is null
                            && string.Equals(id, "KnockBox.Core", StringComparison.OrdinalIgnoreCase))
                        {
                            coreVersion = parsedVersion;
                        }

                        // Only consider entries that actually have a matching DLL on disk
                        // in the plugin folder. Framework/runtime refs and project-only
                        // entries without a co-located DLL are skipped silently — they're
                        // not shareable across ALCs from a plugin folder anyway.
                        var candidateDll = Path.Combine(pluginDir, id + ".dll");
                        if (File.Exists(candidateDll))
                        {
                            (dependencies ??= new List<PluginDependency>())
                                .Add(new PluginDependency(id, parsedVersion, candidateDll));
                        }
                    }
                }

                return new DepsInspection(
                    forbidden,
                    coreVersion,
                    (IReadOnlyList<PluginDependency>?)dependencies ?? Array.Empty<PluginDependency>());
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to parse .deps.json for plugin [{Dll}]; forbidden-dependency and Core-version checks will be skipped.",
                    pluginDllPath);
                return new DepsInspection(null, null);
            }
        }

        /// <summary>
        /// A dependency winner chosen by <see cref="SelectShareableWinners"/>: the
        /// highest-patch version of a library multiple plugins requested at the same
        /// <c>Major.Minor</c>. <see cref="RequesterCount"/> is the number of plugins
        /// that asked for a compatible version (always &gt;= 2).
        /// </summary>
        internal readonly record struct SharedDependencyWinner(
            string SimpleName,
            Version Version,
            string DllPath,
            int RequesterCount);

        /// <summary>
        /// Pure grouping logic for the share decision. Given the per-plugin dependency
        /// lists (and the strong-name token of each candidate DLL), groups by
        /// <c>(SimpleName, Major, Minor, PublicKeyToken)</c> and returns one winner per
        /// group that has at least two distinct plugins requesting it. The winner is
        /// the highest version in the group (patch bumps are non-breaking under
        /// semver). Names already in <paramref name="hostAssemblyNames"/> are skipped
        /// — those are shared by the existing default-ALC fallback path.
        /// </summary>
        internal static IReadOnlyList<SharedDependencyWinner> SelectShareableWinners(
            IReadOnlyList<IReadOnlyList<PluginDependency>> perPluginDependencies,
            Func<string, byte[]?> getPublicKeyToken,
            HashSet<string> hostAssemblyNames)
        {
            // Group key = (name, major, minor, publicKeyTokenHex). Strong-named
            // assemblies with different tokens but the same simple name MUST NOT
            // be considered the same library.
            var groups = new Dictionary<(string Name, int Major, int Minor, string TokenHex),
                (PluginDependency Best, HashSet<int> PluginIndexes)>(
                comparer: new ShareGroupKeyComparer());

            for (int i = 0; i < perPluginDependencies.Count; i++)
            {
                foreach (var dep in perPluginDependencies[i])
                {
                    if (hostAssemblyNames.Contains(dep.SimpleName))
                        continue;
                    if (ForbiddenPluginDependencies.Contains(dep.SimpleName))
                        continue;

                    var token = getPublicKeyToken(dep.DllPath);
                    var tokenHex = token is null || token.Length == 0
                        ? string.Empty
                        : Convert.ToHexString(token);

                    var key = (dep.SimpleName, dep.Version.Major, dep.Version.Minor, tokenHex);
                    if (!groups.TryGetValue(key, out var entry))
                    {
                        entry = (dep, new HashSet<int> { i });
                        groups[key] = entry;
                        continue;
                    }

                    entry.PluginIndexes.Add(i);
                    if (dep.Version > entry.Best.Version)
                        entry.Best = dep;
                    groups[key] = entry;
                }
            }

            var winners = new List<SharedDependencyWinner>();
            foreach (var (_, entry) in groups)
            {
                if (entry.PluginIndexes.Count < 2)
                    continue;
                winners.Add(new SharedDependencyWinner(
                    entry.Best.SimpleName,
                    entry.Best.Version,
                    entry.Best.DllPath,
                    entry.PluginIndexes.Count));
            }
            return winners;
        }

        private sealed class ShareGroupKeyComparer
            : IEqualityComparer<(string Name, int Major, int Minor, string TokenHex)>
        {
            public bool Equals(
                (string Name, int Major, int Minor, string TokenHex) x,
                (string Name, int Major, int Minor, string TokenHex) y) =>
                string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
                && x.Major == y.Major
                && x.Minor == y.Minor
                && string.Equals(x.TokenHex, y.TokenHex, StringComparison.Ordinal);

            public int GetHashCode((string Name, int Major, int Minor, string TokenHex) obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                    obj.Major,
                    obj.Minor,
                    obj.TokenHex);
        }

        /// <summary>
        /// Discovers dependencies shipped by two or more plugins at semver-compatible
        /// versions (same <c>Major.Minor</c>) and loads each winner into the default
        /// <see cref="AssemblyLoadContext"/>. Adds promoted simple names to
        /// <paramref name="hostAssemblyNames"/> so the existing
        /// <c>IsSharedContract</c> predicate routes those names to the default ALC
        /// when the per-plugin contexts are later asked to resolve them. Promotion
        /// failures (bad image, file lock, etc.) are logged and skipped — the
        /// dependency falls back to per-plugin private loading exactly as before.
        /// </summary>
        internal void PromoteShareableDependencies(
            IReadOnlyList<string> pluginSubdirs,
            HashSet<string> hostAssemblyNames)
        {
            if (pluginSubdirs.Count < 2)
                return;

            var perPluginDeps = new List<IReadOnlyList<PluginDependency>>(pluginSubdirs.Count);
            foreach (var subdir in pluginSubdirs)
            {
                var manifestPath = Path.Combine(subdir, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    perPluginDeps.Add(Array.Empty<PluginDependency>());
                    continue;
                }
                var manifestResult = PluginManifest.TryReadFromFile(manifestPath);
                if (!manifestResult.TryGetSuccess(out var manifest))
                {
                    perPluginDeps.Add(Array.Empty<PluginDependency>());
                    continue;
                }
                var dllPath = Path.Combine(subdir, manifest.EntryAssembly + ".dll");
                if (!File.Exists(dllPath))
                {
                    perPluginDeps.Add(Array.Empty<PluginDependency>());
                    continue;
                }
                var inspection = InspectDepsJson(dllPath);
                // Exclude the plugin's own entry assembly — it's never a shareable dep.
                var filtered = inspection.Dependencies
                    .Where(d => !string.Equals(d.SimpleName, manifest.EntryAssembly, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                perPluginDeps.Add(filtered);
            }

            var winners = SelectShareableWinners(
                perPluginDeps,
                getPublicKeyToken: ReadPublicKeyTokenSafe,
                hostAssemblyNames);

            foreach (var winner in winners)
            {
                try
                {
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(winner.DllPath);
                    hostAssemblyNames.Add(winner.SimpleName);
                    logger.LogInformation(
                        "Promoted shared dependency [{Name}] v{Version} into default ALC (requested by {Count} plugins).",
                        winner.SimpleName,
                        winner.Version,
                        winner.RequesterCount);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to promote shared dependency [{Name}] v{Version} from [{Path}]; " +
                        "it will continue to load privately per plugin.",
                        winner.SimpleName,
                        winner.Version,
                        winner.DllPath);
                }
            }
        }

        private byte[]? ReadPublicKeyTokenSafe(string dllPath)
        {
            try
            {
                return AssemblyName.GetAssemblyName(dllPath).GetPublicKeyToken();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Could not read AssemblyName for [{Path}]; treating as unsigned for share-grouping.",
                    dllPath);
                return null;
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
            var assembly = typeof(IPluginModule).Assembly;
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

        private IPluginModule? TryActivate(Type moduleType)
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
                        "Plugin module [{Type}] from [{Assembly}] exceeded the {Timeout:g} activation timeout; skipping.",
                        moduleType.FullName,
                        moduleType.Assembly.GetName().Name,
                        ModuleActivationTimeout);
                    return null;
                }

                if (activation.Result is IPluginModule module)
                    return module;

                logger.LogError(
                    "Type [{Type}] implements IPluginModule but could not be activated as one.",
                    moduleType.FullName);
                return null;
            }
            catch (AggregateException agg) when (agg.InnerException is not null)
            {
                logger.LogError(
                    agg.InnerException,
                    "Failed to activate plugin module [{Type}] from [{Assembly}]. " +
                    "Ensure it has a public parameterless constructor.",
                    moduleType.FullName,
                    moduleType.Assembly.GetName().Name);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to activate plugin module [{Type}] from [{Assembly}]. " +
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
            if (onDisk.Kind != fromModule.Kind)
            { disagreement = $"Kind disk=[{onDisk.Kind}] code=[{fromModule.Kind}]"; return false; }
            if (!onDisk.ExportedContracts
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(fromModule.ExportedContracts))
            {
                disagreement =
                    $"ExportedContracts disk=[{string.Join(',', onDisk.ExportedContracts)}] " +
                    $"code=[{string.Join(',', fromModule.ExportedContracts)}]";
                return false;
            }
            if (!string.Equals(onDisk.ClientAssembly, fromModule.ClientAssembly, StringComparison.Ordinal))
            { disagreement = $"ClientAssembly disk=[{onDisk.ClientAssembly}] code=[{fromModule.ClientAssembly}]"; return false; }
            if (!onDisk.ClientContracts
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(fromModule.ClientContracts))
            {
                disagreement =
                    $"ClientContracts disk=[{string.Join(',', onDisk.ClientContracts)}] " +
                    $"code=[{string.Join(',', fromModule.ClientContracts)}]";
                return false;
            }
            // NOTE: ClientAssets (SHA-256 hashes) are deliberately NOT compared. The
            // embedded plugin.json is compiled before the build stages the client
            // DLLs and computes their hashes, so embedded (code) hashes are empty
            // while on-disk hashes are populated — an intentional asymmetry. Identity
            // (ClientAssembly/ClientContracts) is verified here; integrity is verified
            // by the client against the served hashes.

            disagreement = string.Empty;
            return true;
        }
    }
}
