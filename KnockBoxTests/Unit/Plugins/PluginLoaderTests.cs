using System.Reflection;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBoxTests.Unit.Plugins
{
    [TestClass]
    public class PluginLoaderTests
    {
        private Mock<ILogger<PluginLoader>> _logger = null!;
        private PluginLoader _loader = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = new Mock<ILogger<PluginLoader>>();
            _loader = new PluginLoader(_logger.Object);
        }

        [TestMethod]
        public void LoadModules_MissingDirectory_ReturnsEmptyAndLogsWarning()
        {
            var missing = Path.Combine(Path.GetTempPath(), "knockbox-test-missing-" + Guid.NewGuid());

            var result = _loader.LoadModules(missing);

            Assert.IsEmpty(result.Modules);
            Assert.IsEmpty(result.Assemblies);
            VerifyLogged(LogLevel.Warning, Times.Once());
        }

        [TestMethod]
        public void LoadModules_SelfHostedAssembly_FindsValidModule()
        {
            // Stage this test assembly into a temp "games" directory so the
            // loader scans it via its public API surface.
            //
            // Assertions match on RouteIdentifier (a string), not on the
            // concrete type: plugins load into a per-plugin PluginLoadContext,
            // so the type identity of ValidFakeModule in the ALC differs from
            // this test's default-ALC view. The IGameModule contract itself
            // is a shared-contract interface, so the route-id string is the
            // stable identity across the boundary.
            var tempDir = CreateStagingDirWithThisAssembly();
            try
            {
                var result = _loader.LoadModules(tempDir);

                Assert.Contains(
                    m => m.RouteIdentifier == ValidRoute, result.Modules,
                    "Expected ValidFakeModule (by route id) to be discovered.");
            }
            finally
            {
                TryDelete(tempDir);
            }
        }

        [TestMethod]
        public void LoadModules_DuplicateRouteIdentifier_KeepsFirstAndLogsError()
        {
            var tempDir = CreateStagingDirWithThisAssembly();
            try
            {
                var result = _loader.LoadModules(tempDir);

                var dupMatches = result.Modules
                    .Where(m => string.Equals(m.RouteIdentifier, DuplicateRoute, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Assert.HasCount(1, dupMatches,
                    "Only one module per duplicate route should be kept.");
                VerifyLogged(LogLevel.Error, Times.AtLeastOnce());
            }
            finally
            {
                TryDelete(tempDir);
            }
        }

        [TestMethod]
        public void LoadModules_ActivationFailure_LogsAndSkipsButOtherModulesSurvive()
        {
            var tempDir = CreateStagingDirWithThisAssembly();
            try
            {
                var result = _loader.LoadModules(tempDir);

                Assert.DoesNotContain(
                    m => m.RouteIdentifier == ThrowingRoute, result.Modules,
                    "Module whose ctor throws should be skipped.");
                Assert.Contains(
                    m => m.RouteIdentifier == ValidRoute, result.Modules,
                    "Valid modules should still load when another module's ctor throws.");
                VerifyLogged(LogLevel.Error, Times.AtLeastOnce());
            }
            finally
            {
                TryDelete(tempDir);
            }
        }

        [TestMethod]
        public void LoadModules_FailedAssemblyLoad_LogsErrorAndContinues()
        {
            var tempDir = CreateStagingDirWithThisAssembly();
            try
            {
                // Drop a bogus dll in its own subdirectory matching the loader's
                // expected per-plugin layout (subdir/{subdir}.dll).
                var bogusSubdir = Path.Combine(tempDir, "not-an-assembly");
                Directory.CreateDirectory(bogusSubdir);
                File.WriteAllText(Path.Combine(bogusSubdir, "not-an-assembly.dll"),
                    "this is not a valid assembly");

                var result = _loader.LoadModules(tempDir);

                // Valid modules still come through.
                Assert.Contains(
                    m => m.RouteIdentifier == ValidRoute, result.Modules);
                VerifyLogged(LogLevel.Error, Times.AtLeastOnce());
            }
            finally
            {
                TryDelete(tempDir);
            }
        }

        // --- helpers ---

        private const string ValidRoute = "valid-fake";
        private const string DuplicateRoute = "duplicate-route";
        private const string ThrowingRoute = "throws";

        private static string CreateStagingDirWithThisAssembly()
        {
            var dir = Path.Combine(Path.GetTempPath(), "knockbox-plugin-test-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);

            // Stage under the per-plugin subdirectory layout required by the
            // loader: {plugins-root}/{AssemblyName}/{AssemblyName}.dll.
            var source = typeof(PluginLoaderTests).Assembly.Location;
            var assemblyFileName = Path.GetFileNameWithoutExtension(source);
            var pluginSubdir = Path.Combine(dir, assemblyFileName);
            Directory.CreateDirectory(pluginSubdir);
            File.Copy(source, Path.Combine(pluginSubdir, assemblyFileName + ".dll"), overwrite: true);
            return dir;
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        private void VerifyLogged(LogLevel level, Times times)
        {
            _logger.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);
        }

        // --- Fake modules discovered via reflection on this test assembly ---

        public sealed class ValidFakeModule : IGameModule
        {
            public string Name => "Valid Fake";
            public string Description => "Used by PluginLoader tests.";
            public string RouteIdentifier => ValidRoute;
            public void RegisterServices(IServiceCollection services) { }
            public RenderFragment GetButtonContent() => _ => { };
        }

        public sealed class DuplicateRouteModuleA : IGameModule
        {
            public string Name => "Dup A";
            public string Description => "First of two duplicates.";
            public string RouteIdentifier => DuplicateRoute;
            public void RegisterServices(IServiceCollection services) { }
            public RenderFragment GetButtonContent() => _ => { };
        }

        public sealed class DuplicateRouteModuleB : IGameModule
        {
            public string Name => "Dup B";
            public string Description => "Second of two duplicates.";
            public string RouteIdentifier => DuplicateRoute;
            public void RegisterServices(IServiceCollection services) { }
            public RenderFragment GetButtonContent() => _ => { };
        }

        public sealed class ThrowingCtorModule : IGameModule
        {
            public ThrowingCtorModule() => throw new InvalidOperationException("boom");
            public string Name => "Throws";
            public string Description => "Ctor throws.";
            public string RouteIdentifier => ThrowingRoute;
            public void RegisterServices(IServiceCollection services) { }
            public RenderFragment GetButtonContent() => _ => { };
        }
    }
}
