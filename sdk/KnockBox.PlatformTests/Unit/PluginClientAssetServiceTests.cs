using System.Security.Cryptography;
using KnockBox.Platform;
using KnockBox.Platform.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for <see cref="PluginClientAssetService"/>: it must serve the
/// BUILD-TIME hash from the staged sidecar (never recompute when one exists),
/// fall back to the lone-DLL convention, and keep its path-escape guards.
/// </summary>
[TestClass]
public sealed class PluginClientAssetServiceTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init() => _root = Directory.CreateTempSubdirectory("kbx-assets-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Creates a plugin folder with plugin.json + a client/ dir; returns the client dir.</summary>
    private string CreatePlugin(string route, string dllName, byte[] dllBytes, string? sidecarJson)
    {
        var pluginDir = Path.Combine(_root, route);
        var clientDir = Path.Combine(pluginDir, "client");
        Directory.CreateDirectory(clientDir);

        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), $$"""
            {
                "schemaVersion": 1,
                "name": "Asset Test",
                "description": "d",
                "routeIdentifier": "{{route}}",
                "version": "1.0.0",
                "entryAssembly": "Server.Assembly"
            }
            """);

        File.WriteAllBytes(Path.Combine(clientDir, dllName + ".dll"), dllBytes);
        if (sidecarJson is not null)
            File.WriteAllText(Path.Combine(clientDir, "assets.sha256.json"), sidecarJson);

        return clientDir;
    }

    private PluginClientAssetService BuildService()
    {
        var options = new KnockBoxPlatformOptions();
        options.PluginsPaths.Clear();
        options.PluginsPaths.Add(_root);
        return new PluginClientAssetService(options, NullLogger<PluginClientAssetService>.Instance);
    }

    [TestMethod]
    public void TryGetManifest_ReadsBuildTimeHash_WithoutRecomputing()
    {
        // The sidecar hash deliberately does NOT match the DLL bytes; the service
        // must surface the sidecar value verbatim, proving it doesn't re-hash.
        var fakeHash = new string('a', 64);
        CreatePlugin("asset-a", "Foo.Client", [1, 2, 3, 4],
            sidecarJson: $$"""{ "Foo.Client": "{{fakeHash}}" }""");

        var service = BuildService();

        Assert.IsTrue(service.TryGetManifest("asset-a", out var manifest));
        Assert.AreEqual("Foo.Client", manifest.EntryAssembly);
        Assert.AreEqual(fakeHash, manifest.Sha256);
    }

    [TestMethod]
    public void TryGetManifest_NoSidecar_FallsBackToComputingHash()
    {
        var bytes = new byte[] { 9, 8, 7, 6, 5 };
        CreatePlugin("asset-b", "Bar.Client", bytes, sidecarJson: null);
        var expected = Convert.ToHexString(SHA256.HashData(bytes));

        var service = BuildService();

        Assert.IsTrue(service.TryGetManifest("asset-b", out var manifest));
        Assert.AreEqual("Bar.Client", manifest.EntryAssembly);
        Assert.AreEqual(expected, manifest.Sha256);
    }

    [TestMethod]
    public void TryGetManifest_SidecarMissingEntryHash_ReturnsFalse()
    {
        CreatePlugin("asset-c", "Baz.Client", [1],
            sidecarJson: """{ "SomeOther.Assembly": "abc" }""");

        var service = BuildService();

        Assert.IsFalse(service.TryGetManifest("asset-c", out var manifest));
        Assert.IsNull(manifest);
    }

    [TestMethod]
    public void TryGetManifest_UnknownRoute_ReturnsFalse()
    {
        var service = BuildService();

        Assert.IsFalse(service.TryGetManifest("nope", out _));
    }

    [TestMethod]
    public void TryGetAssemblyPath_ValidName_ResolvesStagedDll()
    {
        CreatePlugin("asset-d", "Qux.Client", [1, 2], sidecarJson: null);

        var service = BuildService();

        Assert.IsTrue(service.TryGetAssemblyPath("asset-d", "Qux.Client", out var path));
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("dir/sub")]
    [DataRow("bad name")]
    public void TryGetAssemblyPath_InvalidName_ReturnsFalse(string assemblyName)
    {
        CreatePlugin("asset-e", "Ok.Client", [1], sidecarJson: null);

        var service = BuildService();

        Assert.IsFalse(service.TryGetAssemblyPath("asset-e", assemblyName, out var path));
        Assert.IsNull(path);
    }
}
