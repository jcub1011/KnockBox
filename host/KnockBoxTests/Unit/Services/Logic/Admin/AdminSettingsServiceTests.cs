using KnockBox.Platform.Storage;
using KnockBox.Services.Logic.Admin;
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
        public void DefaultState_IsDisabled_And_PasswordIsDefault()
        {
            var service = CreateService();
            Assert.IsFalse(service.GetEnableThirdPartyPlugins());
            Assert.IsTrue(service.IsPasswordDefault());
            Assert.IsTrue(service.VerifyPassword("changeme"));
            Assert.IsFalse(service.VerifyPassword("wrong"));
        }

        [TestMethod]
        public async Task PersistsToDisk_AndReloads_IncludingPassword()
        {
            var service1 = CreateService();
            await service1.SetEnableThirdPartyPluginsAsync(true);
            await service1.UpdatePasswordAsync("new-password");
            
            Assert.IsTrue(service1.GetEnableThirdPartyPlugins());
            Assert.IsFalse(service1.IsPasswordDefault());
            Assert.IsTrue(service1.VerifyPassword("new-password"));

            // Create new instance to verify reload
            var service2 = CreateService();
            Assert.IsTrue(service2.GetEnableThirdPartyPlugins());
            Assert.IsFalse(service2.IsPasswordDefault());
            Assert.IsTrue(service2.VerifyPassword("new-password"));
            Assert.IsFalse(service2.VerifyPassword("changeme"), "Old bootstrap password should no longer work.");
        }

        [TestMethod]
        public async Task EmergencyReset_ByDeletingFile_RevertsToDefault()
        {
            var service1 = CreateService();
            await service1.UpdatePasswordAsync("secret");
            Assert.IsFalse(service1.IsPasswordDefault());

            // Simulate emergency reset by deleting the settings file
            var path = Path.Combine(_tempRoot, _settingsFileName);
            File.Delete(path);

            var service2 = CreateService();
            Assert.IsTrue(service2.IsPasswordDefault(), "Should revert to default after file deletion.");
            Assert.IsTrue(service2.VerifyPassword("changeme"));
        }

        private IAdminSettingsService CreateService()
        {
            var options = Options.Create(new AdminOptions 
            { 
                SettingsPath = _settingsFileName,
                Password = "changeme" 
            });
            return new AdminSettingsService(
                _storagePathMock.Object,
                options,
                NullLogger<AdminSettingsService>.Instance);
        }
    }
}
