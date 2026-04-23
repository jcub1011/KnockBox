using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
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
    public RenderFragment GetButtonContent() => _ => { };
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
    public RenderFragment GetButtonContent() => _ => { };
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

    /// <summary>
    /// Guard: if a future change adds another IGameModule to this assembly,
    /// these tests' fixture is no longer isolated and assertions about counts
    /// by RouteIdentifier may silently over- or under-count. Fail fast with a
    /// clear remediation hint instead.
    /// </summary>
    private static void AssertFixtureIsolation()
    {
        var moduleTypesInAssembly = typeof(PluginLoaderTests).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IGameModule).IsAssignableFrom(t))
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                typeof(TestPluginModuleA),
                typeof(TestPluginModuleThrowingCtor),
            },
            moduleTypesInAssembly,
            "PluginLoaderTests fixture is no longer isolated -- the test assembly declares " +
            "IGameModule types beyond the known fixtures. Move new IGameModule test types into " +
            "a dedicated fixture assembly, or scope their RouteIdentifier to a nested-class " +
            "fixture, before relying on these tests.");
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
}
