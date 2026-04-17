using KnockBox.Services.Logic.Admin;
using KnockBox.Services.Logic.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace KnockBox.Tests.Unit.Services.Logic.Admin
{
    [TestClass]
    public sealed class AdminSettingsServiceTests
    {
        private string _tempRoot = null!;
        private string _settingsFileName = "test-settings.json";
        private Mock<IStoragePathService> _storagePathMock = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "KnockBoxTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempRoot);

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
        public void DefaultState_IsDisabled()
        {
            var service = CreateService();
            Assert.IsFalse(service.GetEnableThirdPartyPlugins());
        }

        [TestMethod]
        public async Task PersistsToDisk_AndReloads()
        {
            var service1 = CreateService();
            await service1.SetEnableThirdPartyPluginsAsync(true);
            Assert.IsTrue(service1.GetEnableThirdPartyPlugins());

            // Create new instance to verify reload
            var service2 = CreateService();
            Assert.IsTrue(service2.GetEnableThirdPartyPlugins());
        }

        [TestMethod]
        public async Task SetToSameValue_DoesNotWriteToDisk()
        {
            var service = CreateService();
            var path = Path.Combine(_tempRoot, _settingsFileName);

            await service.SetEnableThirdPartyPluginsAsync(false);
            Assert.IsFalse(File.Exists(path), "Should not create file for default value if not changed.");

            await service.SetEnableThirdPartyPluginsAsync(true);
            var firstWriteTime = File.GetLastWriteTimeUtc(path);

            await Task.Delay(10); // Ensure timestamp can change
            await service.SetEnableThirdPartyPluginsAsync(true);
            var secondWriteTime = File.GetLastWriteTimeUtc(path);

            Assert.AreEqual(firstWriteTime, secondWriteTime, "Should not rewrite file if value is identical.");
        }

        private IAdminSettingsService CreateService()
        {
            var options = Options.Create(new AdminOptions { SettingsPath = _settingsFileName });
            return new AdminSettingsService(
                _storagePathMock.Object,
                options,
                NullLogger<AdminSettingsService>.Instance);
        }
    }
}
