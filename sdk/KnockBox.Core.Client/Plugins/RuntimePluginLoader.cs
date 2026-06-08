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

            var assembly = await LoadAssemblyAsync(routeIdentifier, manifest, ct);
            if (assembly is null)
                return GameRootLoadResult.Failure($"Failed to load client assembly [{manifest.EntryAssembly}].");

            var rootTypeName = $"{manifest.RootNamespace}.{GameRootTypeName}";
            var rootType = assembly.GetType(rootTypeName);
            if (rootType is null)
                return GameRootLoadResult.Failure($"Root component [{rootTypeName}] not found in [{manifest.EntryAssembly}].");
            if (!typeof(IComponent).IsAssignableFrom(rootType))
                return GameRootLoadResult.Failure($"Type [{rootTypeName}] does not implement IComponent.");

            logger.LogInformation(
                "Loaded runtime game UI [{Assembly}] for route [{Route}]; root [{Root}].",
                manifest.EntryAssembly, routeIdentifier, rootTypeName);
            return GameRootLoadResult.Success(rootType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load runtime game UI for route [{Route}].", routeIdentifier);
            return GameRootLoadResult.Failure(ex.Message);
        }
    }

    private async Task<Assembly?> LoadAssemblyAsync(
        string routeIdentifier, ClientPluginManifest manifest, CancellationToken ct)
    {
        if (_loaded.TryGetValue(manifest.EntryAssembly, out var cached))
            return cached;

        var dllUri = $"_plugins/{routeIdentifier}/client/{manifest.EntryAssembly}.dll";
        var bytes = await http.GetByteArrayAsync(dllUri, ct);

        // Integrity gate: verify the downloaded bytes against the server-declared
        // hash BEFORE handing arbitrary IL to the runtime. Runtime-streamed plugin
        // DLLs don't get the framework's automatic SRI, so we restore it here.
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Integrity check failed for [{Assembly}]: expected {Expected}, got {Actual}.",
                manifest.EntryAssembly, manifest.Sha256, actual);
            return null;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        return _loaded.GetOrAdd(manifest.EntryAssembly, assembly);
    }
}
