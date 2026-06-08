using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnockBox.Core.Client.Plugins;
using KnockBox.Core.Plugins;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Maps a game's <c>routeIdentifier</c> to its staged client-UI assets
/// (<c>{pluginFolder}/client/*.dll</c>) and produces the integrity manifest the
/// browser verifies before loading runtime IL. This is the server side of the
/// runtime-third-party-UI path: it exposes the bytes of an assembly the WASM
/// client never referenced at build time.
/// </summary>
public interface IPluginClientAssetService
{
    /// <summary>Builds the client manifest (entry assembly, root namespace, SHA-256) for a route.</summary>
    bool TryGetManifest(string routeIdentifier, [NotNullWhen(true)] out ClientPluginManifest? manifest);

    /// <summary>Resolves the on-disk path of a client assembly for a route, with path-escape guards.</summary>
    bool TryGetAssemblyPath(string routeIdentifier, string assemblyName, [NotNullWhen(true)] out string? path);
}

public sealed partial class PluginClientAssetService : IPluginClientAssetService
{
    private const string ClientSubfolder = "client";
    private const string HashSidecarFile = "assets.sha256.json";

    // route -> absolute plugin folder containing plugin.json
    private readonly ConcurrentDictionary<string, string> _routeToFolder =
        new(StringComparer.OrdinalIgnoreCase);
    // route -> declared client entry assembly (from plugin.json), if any
    private readonly ConcurrentDictionary<string, string> _routeToClientAssembly =
        new(StringComparer.OrdinalIgnoreCase);
    // client dir -> build-time hash map (null sentinel: no sidecar present)
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>?> _sidecarByDir =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PluginClientAssetService> _logger;

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex AssemblyNameRegex();

    public PluginClientAssetService(KnockBoxPlatformOptions options, ILogger<PluginClientAssetService> logger)
    {
        _logger = logger;

        foreach (var rawRoot in options.PluginsPaths)
        {
            var root = ResolvePath(rawRoot);
            if (!Directory.Exists(root))
                continue;

            foreach (var folder in Directory.GetDirectories(root))
            {
                var manifestPath = Path.Combine(folder, PluginManifest.EmbeddedResourceName);
                if (!File.Exists(manifestPath))
                    continue;

                var manifestResult = PluginManifest.TryReadFromFile(manifestPath);
                if (!manifestResult.TryGetSuccess(out var manifest))
                    continue;

                if (Directory.Exists(Path.Combine(folder, ClientSubfolder)))
                {
                    _routeToFolder[manifest.RouteIdentifier] = Path.GetFullPath(folder);
                    if (!string.IsNullOrEmpty(manifest.ClientAssembly))
                        _routeToClientAssembly[manifest.RouteIdentifier] = manifest.ClientAssembly;
                }
            }
        }
    }

    public bool TryGetManifest(string routeIdentifier, [NotNullWhen(true)] out ClientPluginManifest? manifest)
    {
        manifest = null;
        if (!_routeToFolder.TryGetValue(routeIdentifier, out var folder))
            return false;

        var clientDir = Path.Combine(folder, ClientSubfolder);

        // Entry assembly: prefer the plugin.json-declared clientAssembly; fall back
        // to the lone staged DLL (the Phase 0 spike convention).
        string entryAssembly;
        if (_routeToClientAssembly.TryGetValue(routeIdentifier, out var declared))
        {
            entryAssembly = declared;
        }
        else
        {
            var dll = Directory.EnumerateFiles(clientDir, "*.dll").FirstOrDefault();
            if (dll is null)
                return false;
            entryAssembly = Path.GetFileNameWithoutExtension(dll);
        }

        // Integrity hash: read the BUILD-TIME sidecar (no per-serve hashing). Only
        // when no sidecar exists at all (a plugin staged without the client
        // targets) do we fall back to computing it on serve.
        string sha;
        var sidecar = LoadSidecar(clientDir);
        if (sidecar is not null)
        {
            if (!sidecar.TryGetValue(entryAssembly, out var declaredSha))
            {
                _logger.LogError(
                    "Client assembly [{Assembly}] has no build-time hash in [{Sidecar}] for route [{Route}].",
                    entryAssembly, HashSidecarFile, routeIdentifier);
                return false;
            }
            sha = declaredSha;
        }
        else
        {
            var dllPath = Path.Combine(clientDir, entryAssembly + ".dll");
            if (!File.Exists(dllPath))
                return false;
            _logger.LogDebug(
                "No [{Sidecar}] for route [{Route}]; computing client hash at serve time.",
                HashSidecarFile, routeIdentifier);
            sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dllPath)));
        }

        // Convention: the GameRoot lives in the entry assembly's root namespace
        // (== assembly simple name).
        manifest = new ClientPluginManifest(routeIdentifier, entryAssembly, entryAssembly, sha);
        return true;
    }

    /// <summary>
    /// Loads + caches the build-time hash sidecar for a client dir. Returns
    /// <see langword="null"/> (and caches that) when no sidecar exists.
    /// </summary>
    private IReadOnlyDictionary<string, string>? LoadSidecar(string clientDir)
        => _sidecarByDir.GetOrAdd(clientDir, dir =>
        {
            var path = Path.Combine(dir, HashSidecarFile);
            if (!File.Exists(path))
                return null;

            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                return map is null
                    ? null
                    : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read client hash sidecar [{Path}].", path);
                return null;
            }
        });

    public bool TryGetAssemblyPath(string routeIdentifier, string assemblyName, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (!AssemblyNameRegex().IsMatch(assemblyName))
            return false;
        if (!_routeToFolder.TryGetValue(routeIdentifier, out var folder))
            return false;

        var clientDir = Path.GetFullPath(Path.Combine(folder, ClientSubfolder));
        var candidate = Path.GetFullPath(Path.Combine(clientDir, assemblyName + ".dll"));

        // Reject anything that escapes the client dir (defense in depth on top of
        // the assembly-name regex).
        if (!candidate.StartsWith(clientDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _logger.LogWarning("Rejected client asset request escaping [{ClientDir}]: [{Candidate}].",
                clientDir, candidate);
            return false;
        }
        if (!File.Exists(candidate))
            return false;

        path = candidate;
        return true;
    }

    private static string ResolvePath(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
