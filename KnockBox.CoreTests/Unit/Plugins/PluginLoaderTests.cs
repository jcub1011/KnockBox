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
    public void LoadModules_NonPluginBytesAtTopLevel_DoesNotThrow()
    {
        // A file named like a DLL but containing garbage bytes must not crash
        // the loader; it should be logged and skipped.
        var logger = MakeLogger();
        var loader = new PluginLoader(logger.Object);
        var tempDir = MakeTempDir();

        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "not-a-real-assembly.dll"), [0x00, 0x01, 0x02, 0x03]);

            var result = loader.LoadModules(tempDir);

            Assert.IsEmpty(result.Modules);
            // Loader logged an error for the bad DLL rather than throwing.
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
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
    public void LoadModules_DuplicateRouteIdentifier_KeepsOneAndLogsError()
    {
        // Both TestPluginModuleA and TestPluginModuleDuplicateA share the same
        // RouteIdentifier and live in the same (test) assembly, so a single load
        // pass exercises the duplicate-detection branch.
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
            VerifyLogged(logger, LogLevel.Error, Times.AtLeastOnce());
        }
        finally
        {
            SafeDelete(tempDir);
        }
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
}
