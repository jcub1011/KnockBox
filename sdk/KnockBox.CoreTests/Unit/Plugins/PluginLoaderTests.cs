using KnockBox.Core.Plugins;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.CoreTests.Unit.Plugins;

/// <summary>
/// Internal test-only <see cref="IGameModule"/> implementations used by
/// <see cref="PluginLoaderTests"/> to exercise discovery without spinning up
/// separate plugin assemblies. Each fixture returns a fixed
/// <see cref="IPluginManifest"/>; on-disk plugin.json files are written to
/// match (or deliberately mismatch) those at test time.
/// </summary>
public sealed class TestPluginModuleA : IGameModule
{
    public static readonly IPluginManifest FixtureManifest = new PluginManifest(
        Name: "Test Plugin A",
        Description: "A test plugin module.",
        RouteIdentifier: "pluginloader-tests-route-a",
        Version: new Version(1, 0, 0),
        EntryAssembly: "KnockBox.CoreTests",
        Capabilities: new HashSet<PluginCapability>());

    public IPluginManifest Manifest => FixtureManifest;
    public void RegisterServices(IPluginRegistration registration) { }
}

/// <summary>
/// Library-plugin fixture parallel to <see cref="TestPluginModuleA"/>. Exports
/// no contracts so the library-first ordering test can run without staging
/// contract DLLs. Tests that want to exercise contract promotion write their
/// own one-off manifest pointing at fabricated or host-shipped contract names.
/// </summary>
public sealed class TestLibraryModuleA : ILibraryModule
{
    public static readonly IPluginManifest FixtureManifest = new PluginManifest(
        Name: "Test Library A",
        Description: "A test library plugin module.",
        RouteIdentifier: "pluginloader-tests-route-library-a",
        Version: new Version(1, 0, 0),
        EntryAssembly: "KnockBox.CoreTests",
        Capabilities: new HashSet<PluginCapability>(),
        Kind: PluginKind.Library);

    public IPluginManifest Manifest => FixtureManifest;
    public void RegisterServices(IPluginRegistration registration) { }
}

/// <summary>
/// Fixture whose constructor always throws, used to exercise the
/// <see cref="PluginLoader"/> <c>TryActivate</c> catch branch. Has a
/// distinct RouteIdentifier so it's never the target of any manifest
/// search — the assertion is simply that its activation failure does
/// not poison the scan of its sibling module.
/// </summary>
public sealed class TestPluginModuleThrowingCtor : IGameModule
{
    public TestPluginModuleThrowingCtor() =>
        throw new InvalidOperationException("boom");

    public IPluginManifest Manifest => throw new InvalidOperationException("unreachable");
    public void RegisterServices(IPluginRegistration registration) { }
}

[TestClass]
public sealed class PluginLoaderTests
{
    private static Mock<ILogger<PluginLoader>> MakeLogger() => new(MockBehavior.Loose);

    private static string MakeTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "PluginLoaderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Writes a plugin.json that matches <paramref name="manifest"/> into the
    /// given plugin subdirectory. Tests can override <paramref name="routeIdentifier"/>
    /// or <paramref name="extraJsonBody"/> to exercise mismatch/validation paths.
    /// </summary>
    private static void WriteManifest(
        string pluginSubdir,
        IPluginManifest manifest,
        string? routeIdentifier = null,
        int schemaVersion = 1)
    {
        var route = routeIdentifier ?? manifest.RouteIdentifier;
        var path = Path.Combine(pluginSubdir, "plugin.json");
        File.WriteAllText(path, $$"""
            {
                "schemaVersion": {{schemaVersion}},
                "name": "{{manifest.Name}}",
                "description": "{{manifest.Description}}",
                "routeIdentifier": "{{route}}",
                "version": "{{manifest.Version}}",
                "entryAssembly": "{{manifest.EntryAssembly}}",
                "capabilities": []
            }
            """);
    }

    [TestMethod]
    public void LoadModules_NonExistentDirectory_ReturnsEmpty()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var missingPath = Path.Combine(Path.GetTempPath(), "pluginloader-missing-" + Guid.NewGuid().ToString("N"));

        var result = loader.LoadModules(missingPath);

        Assert.IsEmpty(result.Plugins);
        Assert.IsEmpty(result.Assemblies);
    }

    [TestMethod]
    public void LoadModules_EmptyDirectory_ReturnsEmpty()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            Assert.IsEmpty(result.Assemblies);
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_SubdirectoryMissingManifest_LogsErrorAndSkips()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            // Subdirectory exists but has no plugin.json — must be skipped with an error.
            Directory.CreateDirectory(Path.Combine(tempDir, "GhostPlugin"));

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_MalformedManifest_LogsErrorAndSkips()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var pluginSubdir = Path.Combine(tempDir, "BadPlugin");
            Directory.CreateDirectory(pluginSubdir);
            File.WriteAllText(Path.Combine(pluginSubdir, "plugin.json"), "{ this is not valid json");

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_ManifestWithUnsupportedSchemaVersion_Skips()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var pluginSubdir = Path.Combine(tempDir, "FuturePlugin");
            Directory.CreateDirectory(pluginSubdir);
            WriteManifest(pluginSubdir, TestPluginModuleA.FixtureManifest, schemaVersion: 99);

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_ValidPluginSubdirectory_LoadsMatchingModule()
    {
        AssertFixtureIsolation();

        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);
            var pluginSubdir = Path.Combine(tempDir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            File.Copy(testAssemblyPath, Path.Combine(pluginSubdir, assemblyFileName + ".dll"), overwrite: true);
            WriteManifest(pluginSubdir, TestPluginModuleA.FixtureManifest);

            var result = loader.LoadModules(tempDir);

            Assert.Contains(
                p => p.Manifest.RouteIdentifier == "pluginloader-tests-route-a", result.Plugins,
                "Expected TestPluginModuleA to be discovered.");
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_ManifestRouteDoesNotMatchAnyModule_Skips()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);
            var pluginSubdir = Path.Combine(tempDir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            File.Copy(testAssemblyPath, Path.Combine(pluginSubdir, assemblyFileName + ".dll"), overwrite: true);

            // Route identifier that no IGameModule in the test assembly reports.
            WriteManifest(pluginSubdir, TestPluginModuleA.FixtureManifest, routeIdentifier: "route-nobody-claims");

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_CorruptPrimaryDll_LogsErrorAndSkips()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var pluginName = "BrokenPlugin";
            var pluginSubdir = Path.Combine(tempDir, pluginName);
            Directory.CreateDirectory(pluginSubdir);
            File.WriteAllBytes(
                Path.Combine(pluginSubdir, pluginName + ".dll"),
                [0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);

            // Manifest points to the corrupt DLL via entryAssembly.
            File.WriteAllText(Path.Combine(pluginSubdir, "plugin.json"), $$"""
                {
                    "schemaVersion": 1,
                    "name": "Broken",
                    "description": "Broken.",
                    "routeIdentifier": "broken-plugin",
                    "version": "1.0.0",
                    "entryAssembly": "{{pluginName}}",
                    "capabilities": []
                }
                """);

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            Assert.IsEmpty(result.Assemblies);
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_PluginCompiledAgainstNewerCore_Rejected()
    {
        AssertFixtureIsolation();

        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);
            var pluginSubdir = Path.Combine(tempDir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            var dllPath = Path.Combine(pluginSubdir, assemblyFileName + ".dll");
            File.Copy(testAssemblyPath, dllPath, overwrite: true);
            WriteManifest(pluginSubdir, TestPluginModuleA.FixtureManifest);

            // Plant a deps.json claiming a KnockBox.Core version far newer than the host ships.
            var hostCoreVersion = typeof(IGameModule).Assembly.GetName().Version!;
            var impossiblyNew = new Version(hostCoreVersion.Major + 10, 0, 0, 0);
            var depsJsonPath = Path.ChangeExtension(dllPath, ".deps.json");
            File.WriteAllText(depsJsonPath, $$"""
                {
                    "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
                    "libraries": {
                        "{{assemblyFileName}}/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                        "KnockBox.Core/{{impossiblyNew}}": { "type": "package", "serviceable": true, "sha512": "" }
                    }
                }
                """);

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Plugins);
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_PluginCompiledAgainstOlderCore_StillLoads()
    {
        AssertFixtureIsolation();

        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);
            var pluginSubdir = Path.Combine(tempDir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            var dllPath = Path.Combine(pluginSubdir, assemblyFileName + ".dll");
            File.Copy(testAssemblyPath, dllPath, overwrite: true);
            WriteManifest(pluginSubdir, TestPluginModuleA.FixtureManifest);

            // deps.json declaring a version older than the host's — should still load.
            var depsJsonPath = Path.ChangeExtension(dllPath, ".deps.json");
            File.WriteAllText(depsJsonPath, $$"""
                {
                    "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
                    "libraries": {
                        "{{assemblyFileName}}/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                        "KnockBox.Core/0.0.1": { "type": "package", "serviceable": true, "sha512": "" }
                    }
                }
                """);

            var result = loader.LoadModules(tempDir);

            Assert.Contains(
                p => p.Manifest.RouteIdentifier == "pluginloader-tests-route-a",
                result.Plugins);
        }
        finally { SafeDelete(tempDir); }
    }

    // ─── Library plugin coverage ────────────────────────────────────────────

    [TestMethod]
    public void LoadModules_LibraryAndGameDiscoveredTogether_LibraryAppearsBeforeGame()
    {
        // The on-disk folder name for the game starts with "a-" so directory
        // enumeration returns it before the library folder; the loader must
        // still reorder so libraries come first in PluginLoadResult.Plugins.
        AssertFixtureIsolation();

        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);

            var gameSubdir = Path.Combine(tempDir, "a-" + assemblyFileName + "-game");
            Directory.CreateDirectory(gameSubdir);
            File.Copy(testAssemblyPath, Path.Combine(gameSubdir, assemblyFileName + ".dll"), overwrite: true);
            WriteManifest(gameSubdir, TestPluginModuleA.FixtureManifest);

            var librarySubdir = Path.Combine(tempDir, "z-" + assemblyFileName + "-lib");
            Directory.CreateDirectory(librarySubdir);
            File.Copy(testAssemblyPath, Path.Combine(librarySubdir, assemblyFileName + ".dll"), overwrite: true);
            WriteLibraryManifest(librarySubdir, TestLibraryModuleA.FixtureManifest);

            var result = loader.LoadModules(tempDir);

            var libraryIndex = -1;
            var gameIndex = -1;
            for (int i = 0; i < result.Plugins.Count; i++)
            {
                if (result.Plugins[i].Manifest.RouteIdentifier == TestLibraryModuleA.FixtureManifest.RouteIdentifier)
                    libraryIndex = i;
                else if (result.Plugins[i].Manifest.RouteIdentifier == TestPluginModuleA.FixtureManifest.RouteIdentifier)
                    gameIndex = i;
            }

            Assert.AreNotEqual(-1, libraryIndex, "Library plugin must appear in PluginLoadResult.Plugins.");
            Assert.AreNotEqual(-1, gameIndex, "Game plugin must appear in PluginLoadResult.Plugins.");
            Assert.IsLessThan(gameIndex, libraryIndex,
                "Library plugins must precede game plugins in PluginLoadResult.Plugins so the registration " +
                "pipeline can rely on library services being DI-resolvable when games register.");
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_LibraryWithMissingContractDll_LibraryIsRejected()
    {
        AssertFixtureIsolation();

        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);
            var pluginSubdir = Path.Combine(tempDir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            File.Copy(testAssemblyPath, Path.Combine(pluginSubdir, assemblyFileName + ".dll"), overwrite: true);

            // Manifest declares a contract DLL that doesn't exist on disk —
            // contract promotion must reject the library before activation.
            WriteLibraryManifest(
                pluginSubdir,
                TestLibraryModuleA.FixtureManifest,
                exportedContracts: ["DoesNotExist.Contracts"]);

            var result = loader.LoadModules(tempDir);

            Assert.IsFalse(
                result.Plugins.Any(p => p.Manifest.RouteIdentifier == TestLibraryModuleA.FixtureManifest.RouteIdentifier),
                "Library declaring a missing contract DLL must be dropped from the load result.");
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    [TestMethod]
    public void LoadModules_LibraryExportingHostShippedContract_LibraryIsRejected()
    {
        // The host already ships KnockBox.Core (the test runtime references it).
        // A library plugin that tries to export a contract named "KnockBox.Core"
        // is hijacking a host-owned identity and must be rejected before its
        // module is activated.
        AssertFixtureIsolation();

        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            var testAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(testAssemblyPath);
            var pluginSubdir = Path.Combine(tempDir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            File.Copy(testAssemblyPath, Path.Combine(pluginSubdir, assemblyFileName + ".dll"), overwrite: true);

            // Plant a stub file at KnockBox.Core.dll so the File.Exists check passes
            // and the host-identity collision check is the one that rejects the library.
            File.WriteAllBytes(Path.Combine(pluginSubdir, "KnockBox.Core.dll"), [0x00]);

            WriteLibraryManifest(
                pluginSubdir,
                TestLibraryModuleA.FixtureManifest,
                exportedContracts: ["KnockBox.Core"]);

            var result = loader.LoadModules(tempDir);

            Assert.IsFalse(
                result.Plugins.Any(p => p.Manifest.RouteIdentifier == TestLibraryModuleA.FixtureManifest.RouteIdentifier),
                "Library that tries to export a host-shipped assembly identity must be dropped from the load result.");
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally { SafeDelete(tempDir); }
    }

    /// <summary>
    /// Guard: if a future change adds another <see cref="IGameModule"/> or
    /// <see cref="ILibraryModule"/> to this assembly, these tests' fixture is no
    /// longer isolated and assertions about counts by RouteIdentifier may
    /// silently over- or under-count. Fail fast with a clear remediation hint
    /// instead.
    /// </summary>
    private static void AssertFixtureIsolation()
    {
        var gameModuleTypes = typeof(PluginLoaderTests).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IGameModule).IsAssignableFrom(t))
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                typeof(TestPluginModuleA),
                typeof(TestPluginModuleThrowingCtor),
            },
            gameModuleTypes,
            "PluginLoaderTests fixture is no longer isolated -- the test assembly declares " +
            "IGameModule types beyond the known fixtures. Move new IGameModule test types into " +
            "a dedicated fixture assembly, or scope their RouteIdentifier to a nested-class " +
            "fixture, before relying on these tests.");

        var libraryModuleTypes = typeof(PluginLoaderTests).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ILibraryModule).IsAssignableFrom(t))
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[] { typeof(TestLibraryModuleA) },
            libraryModuleTypes,
            "PluginLoaderTests fixture is no longer isolated -- the test assembly declares " +
            "ILibraryModule types beyond the known fixtures.");
    }

    /// <summary>
    /// Writes a library manifest with explicit <c>kind</c> and
    /// <c>exportedContracts</c>. The base <see cref="WriteManifest"/> only knows
    /// about game manifests; the library-specific tests need a different shape.
    /// </summary>
    private static void WriteLibraryManifest(
        string pluginSubdir,
        IPluginManifest manifest,
        IReadOnlyList<string>? exportedContracts = null)
    {
        var contracts = exportedContracts is { Count: > 0 }
            ? "[" + string.Join(", ", exportedContracts.Select(c => $"\"{c}\"")) + "]"
            : "[]";
        var path = Path.Combine(pluginSubdir, "plugin.json");
        File.WriteAllText(path, $$"""
            {
                "schemaVersion": 1,
                "name": "{{manifest.Name}}",
                "description": "{{manifest.Description}}",
                "routeIdentifier": "{{manifest.RouteIdentifier}}",
                "version": "{{manifest.Version}}",
                "entryAssembly": "{{manifest.EntryAssembly}}",
                "kind": "library",
                "exportedContracts": {{contracts}},
                "capabilities": []
            }
            """);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "Test verification assertion; not a runtime code path.")]
    private static void VerifyLogged(Mock<ILogger<PluginLoader>> logger, LogLevel level, Times times)
    {
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    // ─── InspectDepsJson ───────────────────────────────────────────────────

    [TestMethod]
    public void InspectDepsJson_ReturnsPackageId_WhenDepsJsonListsKnockBoxPlatform()
    {
        var logger = new Mock<ILogger<PluginLoader>>();
        var loader = new PluginLoader(logger.Object);
        var tempDir = Path.Combine(Path.GetTempPath(), "knockbox-pluginloader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "Sample.Plugin.dll");
            File.WriteAllText(dllPath, string.Empty);
            var depsJsonPath = Path.ChangeExtension(dllPath, ".deps.json");
            File.WriteAllText(depsJsonPath, """
                {
                    "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
                    "libraries": {
                        "Sample.Plugin/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                        "KnockBox.Core/1.0.0": { "type": "package", "serviceable": true, "sha512": "" },
                        "KnockBox.Platform/1.0.0": { "type": "package", "serviceable": true, "sha512": "" }
                    }
                }
                """);

            var result = loader.InspectDepsJson(dllPath);

            Assert.AreEqual("KnockBox.Platform", result.ForbiddenDependency);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void InspectDepsJson_ForbiddenDependencyIsNull_WhenDepsJsonListsOnlyCoreAndBcl()
    {
        var logger = new Mock<ILogger<PluginLoader>>();
        var loader = new PluginLoader(logger.Object);
        var tempDir = Path.Combine(Path.GetTempPath(), "knockbox-pluginloader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "Clean.Plugin.dll");
            File.WriteAllText(dllPath, string.Empty);
            var depsJsonPath = Path.ChangeExtension(dllPath, ".deps.json");
            File.WriteAllText(depsJsonPath, """
                {
                    "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
                    "libraries": {
                        "Clean.Plugin/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                        "KnockBox.Core/1.0.0": { "type": "package", "serviceable": true, "sha512": "" }
                    }
                }
                """);

            var result = loader.InspectDepsJson(dllPath);

            Assert.IsNull(result.ForbiddenDependency);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void InspectDepsJson_ReturnsDefault_WhenDepsJsonIsMissing()
    {
        var logger = new Mock<ILogger<PluginLoader>>();
        var loader = new PluginLoader(logger.Object);
        var tempDir = Path.Combine(Path.GetTempPath(), "knockbox-pluginloader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "NoDeps.Plugin.dll");
            File.WriteAllText(dllPath, string.Empty);

            var result = loader.InspectDepsJson(dllPath);

            Assert.IsNull(result.ForbiddenDependency);
            Assert.IsNull(result.CoreVersion);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public void InspectDepsJson_ReturnsDefaultAndLogsWarning_WhenDepsJsonIsMalformed()
    {
        var logger = new Mock<ILogger<PluginLoader>>();
        var loader = new PluginLoader(logger.Object);
        var tempDir = Path.Combine(Path.GetTempPath(), "knockbox-pluginloader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "Broken.Plugin.dll");
            File.WriteAllText(dllPath, string.Empty);
            var depsJsonPath = Path.ChangeExtension(dllPath, ".deps.json");
            File.WriteAllText(depsJsonPath, "{ this is not valid json");

            var result = loader.InspectDepsJson(dllPath);

            Assert.IsNull(result.ForbiddenDependency);
            Assert.IsNull(result.CoreVersion);
            VerifyLogged(logger, LogLevel.Warning, Times.Once());
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    // ─── SelectShareableWinners ────────────────────────────────────────────

    private static PluginLoader.PluginDependency Dep(string name, string version, string path = "") =>
        new(name, Version.Parse(version), path);

    private static IReadOnlyList<IReadOnlyList<PluginLoader.PluginDependency>> PerPlugin(
        params PluginLoader.PluginDependency[][] plugins) =>
        plugins.Select(p => (IReadOnlyList<PluginLoader.PluginDependency>)p).ToList();

    [TestMethod]
    public void SelectShareableWinners_GroupsSameMajorMinor_AndPicksHighestPatch()
    {
        var deps = PerPlugin(
            new[] { Dep("Shared.Lib", "1.2.3", "a") },
            new[] { Dep("Shared.Lib", "1.2.7", "b") });
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            getPublicKeyToken: _ => null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(1, winners.Count);
        Assert.AreEqual("Shared.Lib", winners[0].SimpleName);
        Assert.AreEqual(new Version(1, 2, 7), winners[0].Version);
        Assert.AreEqual("b", winners[0].DllPath);
        Assert.AreEqual(2, winners[0].RequesterCount);
    }

    [TestMethod]
    public void SelectShareableWinners_SkipsGroupsWithOnlyOnePlugin()
    {
        var deps = PerPlugin(
            new[] { Dep("Lonely.Lib", "1.0.0") },
            new[] { Dep("Other.Lib", "1.0.0") });
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            _ => null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void SelectShareableWinners_DifferentMinorVersionsAreNotShared()
    {
        var deps = PerPlugin(
            new[] { Dep("Multi.Lib", "1.2.3") },
            new[] { Dep("Multi.Lib", "1.3.0") });
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            _ => null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void SelectShareableWinners_DifferentMajorVersionsAreNotShared()
    {
        var deps = PerPlugin(
            new[] { Dep("Multi.Lib", "1.2.3") },
            new[] { Dep("Multi.Lib", "2.2.3") });
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            _ => null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void SelectShareableWinners_SkipsNamesAlreadyInHostAssemblies()
    {
        var deps = PerPlugin(
            new[] { Dep("KnockBox.Core", "1.0.0") },
            new[] { Dep("KnockBox.Core", "1.0.0") });
        var hostNames = new HashSet<string>(["KnockBox.Core"], StringComparer.OrdinalIgnoreCase);
        var winners = PluginLoader.SelectShareableWinners(deps, _ => null, hostNames);

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void SelectShareableWinners_SkipsForbiddenDependencies()
    {
        var deps = PerPlugin(
            new[] { Dep("KnockBox.Platform", "1.0.0") },
            new[] { Dep("KnockBox.Platform", "1.0.0") });
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            _ => null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void SelectShareableWinners_DifferentPublicKeyTokensAreNotGrouped()
    {
        var deps = PerPlugin(
            new[] { Dep("Strong.Lib", "1.2.3", "pathA") },
            new[] { Dep("Strong.Lib", "1.2.3", "pathB") });
        byte[] TokenFor(string path) => path == "pathA" ? new byte[] { 1 } : new byte[] { 2 };
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            TokenFor,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void SelectShareableWinners_CountsDistinctPluginsNotDuplicateEntries()
    {
        // Same plugin (index 0) lists the dep twice for some pathological reason — it
        // must not count as two requesters.
        var deps = PerPlugin(
            new[] { Dep("Dup.Lib", "1.2.3"), Dep("Dup.Lib", "1.2.4") });
        var winners = PluginLoader.SelectShareableWinners(
            deps,
            _ => null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.AreEqual(0, winners.Count);
    }

    [TestMethod]
    public void InspectDepsJson_PopulatesDependencies_WhenDllExistsInPluginFolder()
    {
        var logger = new Mock<ILogger<PluginLoader>>();
        var loader = new PluginLoader(logger.Object);
        var tempDir = Path.Combine(Path.GetTempPath(), "knockbox-pluginloader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "Sample.Plugin.dll");
            File.WriteAllText(dllPath, string.Empty);
            // Co-locate a "dependency" DLL on disk so the probe in InspectDepsJson succeeds.
            File.WriteAllText(Path.Combine(tempDir, "Shared.Lib.dll"), string.Empty);
            var depsJsonPath = Path.ChangeExtension(dllPath, ".deps.json");
            File.WriteAllText(depsJsonPath, """
                {
                    "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
                    "libraries": {
                        "Sample.Plugin/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                        "Shared.Lib/1.2.3": { "type": "package", "serviceable": true, "sha512": "" },
                        "Missing.Lib/9.9.9": { "type": "package", "serviceable": true, "sha512": "" }
                    }
                }
                """);

            var result = loader.InspectDepsJson(dllPath);

            // Sample.Plugin.dll exists too, so it's reported; Missing.Lib has no DLL, so it isn't.
            Assert.IsTrue(result.Dependencies.Any(d => d.SimpleName == "Shared.Lib" && d.Version == new Version(1, 2, 3)));
            Assert.IsFalse(result.Dependencies.Any(d => d.SimpleName == "Missing.Lib"));
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
