using KnockBox.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.CoreTests.Unit.Plugins;

/// <summary>
/// Internal test-only <see cref="IGameModule"/> implementations used by
/// <see cref="PluginLoaderTests"/> to exercise discovery without spinning up
/// separate plugin assemblies.
/// </summary>
public sealed class TestPluginModuleA : IGameModule
{
    public string Name => "Test Plugin A";
    public string Description => "A test plugin module.";
    public string RouteIdentifier => "pluginloader-tests-route-a";
    public void RegisterServices(IServiceCollection services) { }
}

public sealed class TestPluginModuleDuplicateA : IGameModule
{
    public string Name => "Test Plugin A (Duplicate)";
    public string Description => "A test plugin module with a duplicate route id.";
    public string RouteIdentifier => "pluginloader-tests-route-a";
    public void RegisterServices(IServiceCollection services) { }
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

    [TestMethod]
    public void LoadModules_NonExistentDirectory_ReturnsEmpty()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var missingPath = Path.Combine(Path.GetTempPath(), "pluginloader-missing-" + Guid.NewGuid().ToString("N"));

        var result = loader.LoadModules(missingPath);

        Assert.IsEmpty(result.Modules);
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

            Assert.IsEmpty(result.Modules);
            Assert.IsEmpty(result.Assemblies);
        }
        finally
        {
            SafeDelete(tempDir);
        }
    }

    [TestMethod]
    public void LoadModules_LooseDllAtTopLevel_IsIgnored()
    {
        // Only per-subdirectory plugin layouts are supported; a DLL placed
        // loose at the plugins root must be ignored entirely (not even
        // inspected), so no error is logged because no load is attempted.
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "not-a-real-assembly.dll"), [0x00, 0x01, 0x02, 0x03]);

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Modules);
            Assert.IsEmpty(result.Assemblies);
        }
        finally
        {
            SafeDelete(tempDir);
        }
    }

    [TestMethod]
    public void LoadModules_SubdirectoryMissingPrimaryDll_LogsWarningAndSkips()
    {
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            // Subdirectory exists but contains no {subdirName}.dll — must be skipped with a warning.
            Directory.CreateDirectory(Path.Combine(tempDir, "GhostPlugin"));

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Modules);
            VerifyLogged(logger, LogLevel.Warning, Times.AtLeastOnce());
        }
        finally
        {
            SafeDelete(tempDir);
        }
    }

    [TestMethod]
    public void LoadModules_ValidPluginSubdirectory_LoadsTestModules()
    {
        // Copies this test assembly into a subdirectory named to match, simulating
        // the per-plugin publish layout, and verifies the loader discovers the
        // IGameModule types defined in this file.
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

            var result = loader.LoadModules(tempDir);

            Assert.Contains(
                m => m.RouteIdentifier == "pluginloader-tests-route-a", result.Modules,
                "Expected TestPluginModuleA to be discovered.");
        }
        finally
        {
            SafeDelete(tempDir);
        }
    }

    [TestMethod]
    public void LoadModules_DuplicateRouteIdentifier_KeepsOneAndLogsErrorNamingBoth()
    {
        // Both TestPluginModuleA and TestPluginModuleDuplicateA share the same
        // RouteIdentifier and live in the same (test) assembly, so a single load
        // pass exercises the duplicate-detection branch.
        //
        // Type.GetTypes() does not guarantee ordering, so we don't assert *which*
        // duplicate wins. Instead we lock in the documented contract: exactly one
        // module survives, and the error log names BOTH type full names so ops
        // can identify the collision from the log alone.
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

            var result = loader.LoadModules(tempDir);

            int matches = result.Modules.Count(m => m.RouteIdentifier == "pluginloader-tests-route-a");
            Assert.AreEqual(1, matches, "Duplicate route identifiers must collapse to a single registration.");

            VerifyErrorLoggedContainingBoth(
                logger,
                typeof(TestPluginModuleA).FullName!,
                typeof(TestPluginModuleDuplicateA).FullName!);
        }
        finally
        {
            SafeDelete(tempDir);
        }
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
            new[] { typeof(TestPluginModuleA), typeof(TestPluginModuleDuplicateA) },
            moduleTypesInAssembly,
            "PluginLoaderTests fixture is no longer isolated -- the test assembly declares " +
            "IGameModule types beyond TestPluginModuleA/TestPluginModuleDuplicateA. Move new " +
            "IGameModule test types into a dedicated fixture assembly, or scope the duplicate " +
            "RouteIdentifier to a nested-class fixture, before relying on these tests.");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "It's not a big deal.")]
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

    /// <summary>
    /// Verifies that at least one Error-level log entry's formatted message
    /// contains both of the given type names. Using the rendered message is
    /// robust to template-argument ordering and lets us pin the "error names
    /// both types" contract without depending on the specific template shape.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "It's not a big deal.")]
    private static void VerifyErrorLoggedContainingBoth(
        Mock<ILogger<PluginLoader>> logger,
        string firstTypeName,
        string secondTypeName)
    {
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains(firstTypeName) &&
                state.ToString()!.Contains(secondTypeName)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }
}
