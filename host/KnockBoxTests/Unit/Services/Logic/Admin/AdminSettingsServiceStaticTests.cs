using KnockBox.Platform.Storage;
using KnockBox.Services.Logic.Admin;
using Moq;

namespace KnockBox.Tests.Unit.Services.Logic.Admin
{
    /// <summary>
    /// Covers <see cref="AdminSettingsService.ReadThirdPartyToggleFromDisk"/>,
    /// which <c>Program.cs</c> calls before the DI container exists. That means
    /// this path runs once per host startup and must stay resilient to a missing
    /// file, a malformed file, and the backup-fallback chain.
    /// </summary>
    [TestClass]
    public sealed class AdminSettingsServiceStaticTests
    {
        private string _tempRoot = null!;
        private const string SettingsFileName = "test-settings.json";
        private AdminOptions _options = null!;
        private Mock<IStoragePathService> _storagePathMock = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "KnockBoxTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempRoot);

            _options = new AdminOptions { SettingsPath = SettingsFileName };

            _storagePathMock = new Mock<IStoragePathService>();
            _storagePathMock.Setup(x => x.GetAdminDirectory()).Returns(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }

        [TestMethod]
        public void ReturnsFalse_WhenFileMissing()
        {
            Assert.IsFalse(AdminSettingsService.ReadThirdPartyToggleFromDisk(_storagePathMock.Object, _options));
        }

        [TestMethod]
        public void ReturnsTrue_FromJson()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, SettingsFileName),
                """{"enableThirdPartyPlugins":true}""");

            Assert.IsTrue(AdminSettingsService.ReadThirdPartyToggleFromDisk(_storagePathMock.Object, _options));
        }

        [TestMethod]
        public void ReturnsFalse_FromJson_WhenExplicitlyFalse()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, SettingsFileName),
                """{"enableThirdPartyPlugins":false}""");

            Assert.IsFalse(AdminSettingsService.ReadThirdPartyToggleFromDisk(_storagePathMock.Object, _options));
        }

        [TestMethod]
        public void FallsBackToBackup_WhenMainMalformed()
        {
            var path = Path.Combine(_tempRoot, SettingsFileName);
            File.WriteAllText(path, "{ not valid json");
            File.WriteAllText(path + ".bak", """{"enableThirdPartyPlugins":true}""");

            Assert.IsTrue(AdminSettingsService.ReadThirdPartyToggleFromDisk(_storagePathMock.Object, _options));
        }

        [TestMethod]
        public void ReturnsFalse_WhenBothMalformed()
        {
            var path = Path.Combine(_tempRoot, SettingsFileName);
            File.WriteAllText(path, "{ not valid json");
            File.WriteAllText(path + ".bak", "{ also not valid");

            Assert.IsFalse(AdminSettingsService.ReadThirdPartyToggleFromDisk(_storagePathMock.Object, _options));
        }

        [TestMethod]
        public void ReturnsFalse_WhenMainMalformedAndNoBackup()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, SettingsFileName),
                "{ not valid json");

            Assert.IsFalse(AdminSettingsService.ReadThirdPartyToggleFromDisk(_storagePathMock.Object, _options));
        }
    }
}
