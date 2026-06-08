using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
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

    // route -> absolute plugin folder containing plugin.json
    private readonly ConcurrentDictionary<string, string> _routeToFolder =
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
                    _routeToFolder[manifest.RouteIdentifier] = Path.GetFullPath(folder);
            }
        }
    }

    public bool TryGetManifest(string routeIdentifier, [NotNullWhen(true)] out ClientPluginManifest? manifest)
    {
        manifest = null;
        if (!_routeToFolder.TryGetValue(routeIdentifier, out var folder))
            return false;

        var clientDir = Path.Combine(folder, ClientSubfolder);
        var dll = Directory.EnumerateFiles(clientDir, "*.dll").FirstOrDefault();
        if (dll is null)
            return false;

        var entryAssembly = Path.GetFileNameWithoutExtension(dll);
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)));

        // Convention for the spike: the GameRoot lives in the entry assembly's
        // root namespace (== assembly simple name).
        manifest = new ClientPluginManifest(routeIdentifier, entryAssembly, entryAssembly, sha);
        return true;
    }

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
