using System.Reflection;
using KnockBox.Core.Plugins;
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

            Assert.AreEqual(0, result.Modules.Count);
            Assert.AreEqual(0, result.Assemblies.Count);
            VerifyLogged(LogLevel.Warning, Times.Once());
        }

        [TestMethod]
        public void LoadModules_SelfHostedAssembly_FindsValidModule()
        {
            // Stage this test assembly into a temp "games" directory so the
            // loader scans it via its public API surface.
            var tempDir = CreateStagingDirWithThisAssembly();
            try
            {
                var result = _loader.LoadModules(tempDir);

                // Must find at least our valid fake module.
                Assert.IsTrue(result.Modules.Any(m => m is ValidFakeModule),
                    "Expected ValidFakeModule to be discovered.");
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

                Assert.AreEqual(1, dupMatches.Count,
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

                Assert.IsFalse(result.Modules.Any(m => m is ThrowingCtorModule),
                    "Module whose ctor throws should be skipped.");
                Assert.IsTrue(result.Modules.Any(m => m is ValidFakeModule),
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
                // Drop a bogus dll that cannot be loaded as an assembly.
                var bogusDll = Path.Combine(tempDir, "not-an-assembly.dll");
                File.WriteAllText(bogusDll, "this is not a valid assembly");

                var result = _loader.LoadModules(tempDir);

                // Valid modules still come through.
                Assert.IsTrue(result.Modules.Any(m => m is ValidFakeModule));
                VerifyLogged(LogLevel.Error, Times.AtLeastOnce());
            }
            finally
            {
                TryDelete(tempDir);
            }
        }

        // --- helpers ---

        private const string DuplicateRoute = "duplicate-route";

        private static string CreateStagingDirWithThisAssembly()
        {
            var dir = Path.Combine(Path.GetTempPath(), "knockbox-plugin-test-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);

            var source = typeof(PluginLoaderTests).Assembly.Location;
            var dest = Path.Combine(dir, Path.GetFileName(source));
            File.Copy(source, dest, overwrite: true);
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
            public string RouteIdentifier => "valid-fake";
            public void RegisterServices(IServiceCollection services) { }
        }

        public sealed class DuplicateRouteModuleA : IGameModule
        {
            public string Name => "Dup A";
            public string Description => "First of two duplicates.";
            public string RouteIdentifier => DuplicateRoute;
            public void RegisterServices(IServiceCollection services) { }
        }

        public sealed class DuplicateRouteModuleB : IGameModule
        {
            public string Name => "Dup B";
            public string Description => "Second of two duplicates.";
            public string RouteIdentifier => DuplicateRoute;
            public void RegisterServices(IServiceCollection services) { }
        }

        public sealed class ThrowingCtorModule : IGameModule
        {
            public ThrowingCtorModule() => throw new InvalidOperationException("boom");
            public string Name => "Throws";
            public string Description => "Ctor throws.";
            public string RouteIdentifier => "throws";
            public void RegisterServices(IServiceCollection services) { }
        }
    }
}
