using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace KnockBox.Core.Client.Plugins;

/// <summary>
/// Default <see cref="IClientPluginLoader"/>. Proves Phase 0 kill-criterion #1:
/// it loads a game-UI assembly that was NOT part of the trimmed client's build
/// graph by streaming its bytes from the host and feeding them to
/// <see cref="AssemblyLoadContext.Default"/> — the only load path that works for
/// a build-time-unknown assembly (<c>LazyAssemblyLoader</c> only knows assemblies
/// declared at publish time in <c>blazor.boot.json</c>).
/// </summary>
public sealed class RuntimePluginLoader(HttpClient http, ILogger<RuntimePluginLoader> logger)
    : IClientPluginLoader
{
    private const string GameRootTypeName = "GameRoot";

    // The browser has effectively ONE load context and is single-threaded in
    // .NET 10. We enforce one resolved assembly per simple-name per session:
    // a second LoadFromStream of an already-loaded simple name returns the cached
    // assembly rather than risking a duplicate/identity-conflicting load.
    private readonly ConcurrentDictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public async Task<GameRootLoadResult> LoadGameRootAsync(string routeIdentifier, CancellationToken ct = default)
    {
        try
        {
            var manifestUri = $"_plugins/{routeIdentifier}/client/manifest.json";
            var manifest = await http.GetFromJsonAsync<ClientPluginManifest>(manifestUri, ct);
            if (manifest is null)
                return GameRootLoadResult.Failure($"No client manifest at [{manifestUri}].");

            // Load dependency assemblies (e.g. the game's {Game}.Contracts DLL) into
            // the default ALC BEFORE the entry assembly, so the entry's references
            // resolve against already-loaded assemblies. The browser has one load
            // context, so this is just sequential LoadFromStream calls.
            foreach (var dep in manifest.Dependencies ?? [])
            {
                var depAssembly = await LoadAssemblyAsync(routeIdentifier, dep.Name, dep.Sha256, ct);
                if (depAssembly is null)
                    return GameRootLoadResult.Failure($"Failed to load client dependency [{dep.Name}].");
            }

            var assembly = await LoadAssemblyAsync(routeIdentifier, manifest.EntryAssembly, manifest.Sha256, ct);
            if (assembly is null)
                return GameRootLoadResult.Failure($"Failed to load client assembly [{manifest.EntryAssembly}].");

            // Prefer an explicit IGameClientModule declaration if the assembly
            // ships one; otherwise fall back to the {RootNamespace}.GameRoot name
            // convention (what the Phase 0 spike uses).
            var rootType = ResolveFromModule(assembly)
                ?? assembly.GetType($"{manifest.RootNamespace}.{GameRootTypeName}");

            if (rootType is null)
            {
                return GameRootLoadResult.Failure(
                    $"No IGameClientModule and no [{manifest.RootNamespace}.{GameRootTypeName}] " +
                    $"component found in [{manifest.EntryAssembly}].");
            }
            if (!typeof(IComponent).IsAssignableFrom(rootType))
                return GameRootLoadResult.Failure($"Type [{rootType.FullName}] does not implement IComponent.");

            logger.LogInformation(
                "Loaded runtime game UI [{Assembly}] for route [{Route}]; root [{Root}].",
                manifest.EntryAssembly, routeIdentifier, rootType.FullName);
            return GameRootLoadResult.Success(rootType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load runtime game UI for route [{Route}].", routeIdentifier);
            return GameRootLoadResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Looks for a public, parameterless <see cref="IGameClientModule"/> in the
    /// loaded assembly and returns its declared root component type. Returns
    /// <see langword="null"/> if the assembly ships no module (the spike case).
    /// </summary>
    private Type? ResolveFromModule(Assembly assembly)
    {
        var moduleType = assembly.GetExportedTypes().FirstOrDefault(t =>
            !t.IsAbstract
            && typeof(IGameClientModule).IsAssignableFrom(t)
            && t.GetConstructor(Type.EmptyTypes) is not null);

        if (moduleType is null)
            return null;

        try
        {
            var module = (IGameClientModule)Activator.CreateInstance(moduleType)!;
            return module.GameRootComponentType;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Found IGameClientModule [{Module}] but could not activate it; " +
                "falling back to the GameRoot name convention.", moduleType.FullName);
            return null;
        }
    }

    private async Task<Assembly?> LoadAssemblyAsync(
        string routeIdentifier, string assemblyName, string expectedSha256, CancellationToken ct)
    {
        if (_loaded.TryGetValue(assemblyName, out var cached))
            return cached;

        var dllUri = $"_plugins/{routeIdentifier}/client/{assemblyName}.dll";
        var bytes = await http.GetByteArrayAsync(dllUri, ct);

        // Integrity gate: verify the downloaded bytes against the server-declared
        // hash BEFORE handing arbitrary IL to the runtime. Runtime-streamed plugin
        // DLLs don't get the framework's automatic SRI, so we restore it here.
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Integrity check failed for [{Assembly}]: expected {Expected}, got {Actual}.",
                assemblyName, expectedSha256, actual);
            return null;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        return _loaded.GetOrAdd(assemblyName, assembly);
    }
}
