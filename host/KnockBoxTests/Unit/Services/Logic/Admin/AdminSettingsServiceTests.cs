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
            Assert.IsTrue(service.VerifyAdminPassword("changeme"));
            Assert.IsFalse(service.VerifyAdminPassword("wrong"));
        }

        [TestMethod]
        public async Task PersistsToDisk_AndReloads_IncludingPassword()
        {
            var service1 = CreateService();
            await service1.SetEnableThirdPartyPluginsAsync(true);
            await service1.SetAdminPasswordAsync("new-password");

            Assert.IsTrue(service1.GetEnableThirdPartyPlugins());
            Assert.IsFalse(service1.IsPasswordDefault());
            Assert.IsTrue(service1.VerifyAdminPassword("new-password"));

            // Create new instance to verify reload
            var service2 = CreateService();
            Assert.IsTrue(service2.GetEnableThirdPartyPlugins());
            Assert.IsFalse(service2.IsPasswordDefault());
            Assert.IsTrue(service2.VerifyAdminPassword("new-password"));
            Assert.IsFalse(service2.VerifyAdminPassword("changeme"), "Old bootstrap password should no longer work.");
        }

        [TestMethod]
        public async Task EmergencyReset_ByDeletingFile_RevertsToDefault()
        {
            var service1 = CreateService();
            await service1.SetAdminPasswordAsync("secret");
            Assert.IsFalse(service1.IsPasswordDefault());

            // Simulate emergency reset by deleting the settings file
            var path = Path.Combine(_tempRoot, _settingsFileName);
            File.Delete(path);

            var service2 = CreateService();
            Assert.IsTrue(service2.IsPasswordDefault(), "Should revert to default after file deletion.");
            Assert.IsTrue(service2.VerifyAdminPassword("changeme"));
        }

        [TestMethod]
        public async Task CorruptedFile_RestoresFromBackup()
        {
            var service1 = CreateService();
            await service1.SetEnableThirdPartyPluginsAsync(true);
            await service1.SetAdminPasswordAsync("secret");

            var path = Path.Combine(_tempRoot, _settingsFileName);
            var backupPath = path + ".bak";

            Assert.IsTrue(File.Exists(backupPath), "Backup file should have been created during persist.");

            // Corrupt the main settings file
            await File.WriteAllTextAsync(path, "{ invalid_json: ");

            // Create a new instance, which should recover from the backup
            var service2 = CreateService();

            Assert.IsTrue(service2.GetEnableThirdPartyPlugins(), "Should have recovered 'true' from backup.");
            Assert.IsTrue(service2.VerifyAdminPassword("secret"), "Should have recovered password from backup.");
        }

        [TestMethod]
        public async Task SetToSameValue_DoesNotWriteToDisk()
        {
            var service = CreateService();
            var path = Path.Combine(_tempRoot, _settingsFileName);

            await service.SetEnableThirdPartyPluginsAsync(false);
            Assert.IsFalse(File.Exists(path), "Default value should not create a file.");

            await service.SetEnableThirdPartyPluginsAsync(true);
            var firstWriteTime = File.GetLastWriteTimeUtc(path);

            await Task.Delay(10);
            await service.SetEnableThirdPartyPluginsAsync(true);
            var secondWriteTime = File.GetLastWriteTimeUtc(path);

            Assert.AreEqual(firstWriteTime, secondWriteTime,
                "Identical value must not rewrite file.");
        }

        [TestMethod]
        public void IsAdminPasswordSet_False_WhenNoPersistedOrDefault()
        {
            var service = CreateService(defaultPassword: "");
            Assert.IsFalse(service.IsAdminPasswordSet());
            Assert.IsFalse(service.VerifyAdminPassword("anything"));
        }

        [TestMethod]
        public void IsAdminPasswordSet_True_WhenDefaultProvided()
        {
            var service = CreateService(defaultPassword: "dev-default");
            Assert.IsTrue(service.IsAdminPasswordSet());
            Assert.IsTrue(service.VerifyAdminPassword("dev-default"));
            Assert.IsFalse(service.VerifyAdminPassword("wrong"));
        }

        [TestMethod]
        public async Task SetAdminPassword_PersistsAndVerifies()
        {
            var service1 = CreateService();
            await service1.SetAdminPasswordAsync("operator-secret");

            Assert.IsTrue(service1.IsAdminPasswordSet());
            Assert.IsTrue(service1.VerifyAdminPassword("operator-secret"));
            Assert.IsFalse(service1.VerifyAdminPassword("bad"));

            var service2 = CreateService();
            Assert.IsTrue(service2.IsAdminPasswordSet());
            Assert.IsTrue(service2.VerifyAdminPassword("operator-secret"));
        }

        [TestMethod]
        public async Task PersistedPassword_OverridesDefault()
        {
            var service1 = CreateService(defaultPassword: "dev-default");
            await service1.SetAdminPasswordAsync("real-secret");

            // Same disk state, but a fresh default is still present.
            var service2 = CreateService(defaultPassword: "dev-default");

            Assert.IsTrue(service2.VerifyAdminPassword("real-secret"));
            Assert.IsFalse(service2.VerifyAdminPassword("dev-default"),
                "Persisted password must shadow the configuration default.");
        }

        [TestMethod]
        public async Task SetAdminPassword_Rejects_NullEmptyOrWhitespace()
        {
            var service = CreateService();

            await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
                await service.SetAdminPasswordAsync(null!));
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await service.SetAdminPasswordAsync(""));
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await service.SetAdminPasswordAsync("   "));
        }

        [TestMethod]
        public void LegacyPlaintextPassword_InJsonPasswordField_IsIgnored()
        {
            // Pre-1.0 format: `password` field at the root. We no longer load it.
            var path = Path.Combine(_tempRoot, _settingsFileName);
            File.WriteAllText(path, """{"enableThirdPartyPlugins":false,"password":"plaintext"}""");

            var service = CreateService(defaultPassword: "");

            Assert.IsTrue(service.IsPasswordDefault(),
                "A legacy `password` field must be ignored — the service must look uninitialized.");
            Assert.IsFalse(service.VerifyAdminPassword("plaintext"),
                "Legacy plaintext values must not authenticate.");
            Assert.IsFalse(service.IsAdminPasswordSet(),
                "No persisted hash and no default — IsAdminPasswordSet must be false.");
        }

        [TestMethod]
        public void LegacyNonV1PasswordHash_IsIgnored_AndLogsWarning()
        {
            // Pre-1.0 interim format: plaintext stored in `passwordHash` as a
            // migration bridge. The bridge has been removed — any non-`v1:` value
            // must be rejected so the operator resets.
            var path = Path.Combine(_tempRoot, _settingsFileName);
            File.WriteAllText(path, """{"enableThirdPartyPlugins":false,"passwordHash":"plaintext"}""");

            var service = CreateService(defaultPassword: "");

            Assert.IsFalse(service.IsPasswordDefault(),
                "A non-empty hash string flips IsPasswordDefault to false — the file is not 'uninitialized', it's unrecognized.");
            Assert.IsFalse(service.VerifyAdminPassword("plaintext"),
                "Non-v1 hash format must not authenticate.");
        }

        [TestMethod]
        public async Task Backup_Mirrors_Main_AfterSetAdminPassword()
        {
            var service = CreateService();
            await service.SetAdminPasswordAsync("operator-secret");

            var path = Path.Combine(_tempRoot, _settingsFileName);
            var backupPath = path + ".bak";

            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(File.Exists(backupPath));

            var mainContents = await File.ReadAllTextAsync(path);
            var backupContents = await File.ReadAllTextAsync(backupPath);

            Assert.AreEqual(mainContents, backupContents,
                "Backup is written *after* the atomic rename, so it must mirror the new state byte-for-byte.");
            Assert.Contains("\"passwordHash\":", mainContents);
            Assert.Contains("\"v1:", mainContents);
        }

        [TestMethod]
        public async Task LoadFromBackup_VerifiesCurrentPassword_NotPrior()
        {
            var service1 = CreateService();
            await service1.SetAdminPasswordAsync("first-secret");
            await service1.SetAdminPasswordAsync("current-secret");

            // Simulate a crash-time scenario: main file is gone, only the backup
            // remains. The backup must hold the *current* password, not "first-secret".
            var path = Path.Combine(_tempRoot, _settingsFileName);
            File.WriteAllText(path, "{ not valid json");

            var service2 = CreateService();

            Assert.IsTrue(service2.VerifyAdminPassword("current-secret"),
                "Backup must hold the most-recently-persisted password.");
            Assert.IsFalse(service2.VerifyAdminPassword("first-secret"),
                "An earlier password must not be recoverable from the backup.");
        }

        [TestMethod]
        public async Task CorruptedFile_AndNoBackup_ThrowsException()
        {
            var service1 = CreateService();
            await service1.SetEnableThirdPartyPluginsAsync(true);

            var path = Path.Combine(_tempRoot, _settingsFileName);
            var backupPath = path + ".bak";

            // Corrupt the main settings file and delete the backup
            await File.WriteAllTextAsync(path, "{ invalid_json: ");
            File.Delete(backupPath);

            try
            {
                CreateService();
                Assert.Fail("Should fail hard if settings are corrupted and no backup exists.");
            }
            catch (System.Text.Json.JsonException)
            {
                // Expected
            }
        }

        private IAdminSettingsService CreateService(string defaultPassword = "changeme")
        {
            var options = Options.Create(new AdminOptions
            {
                SettingsPath = _settingsFileName,
                Password = defaultPassword,
            });
            return new AdminSettingsService(
                _storagePathMock.Object,
                options,
                NullLogger<AdminSettingsService>.Instance);
        }
    }
}
