using KnockBox.Platform.Storage;
using KnockBox.Services.Logic.Admin;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// File-backed implementation of <see cref="IAdminSettingsService"/>.
    ///
    /// Admin password resolution order:
    ///   1. The persisted value in the admin settings file (operator-set via
    ///      the admin UI).
    ///   2. The <c>Admin:Password</c> default from configuration (appsettings
    ///      or <c>Admin__Password</c> env var) — empty string when unset.
    /// If neither is populated the deployment is considered uninitialized.
    /// </summary>
    internal sealed class AdminSettingsService : IAdminSettingsService
    {
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly string _statePath;
        private readonly ILogger _logger;
        private readonly string _defaultPassword;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private bool _enableThirdPartyPlugins;
        private string? _persistedPassword;

        public AdminSettingsService(
            IStoragePathService storagePath,
            IOptions<AdminOptions> options,
            ILogger<AdminSettingsService> logger)
        {
            _logger = logger;
            _statePath = Path.Combine(storagePath.GetAdminDirectory(), options.Value.SettingsPath);
            _defaultPassword = options.Value.Password ?? string.Empty;

            LoadFromDisk();
        }

        public bool GetEnableThirdPartyPlugins() => _enableThirdPartyPlugins;

        public async ValueTask SetEnableThirdPartyPluginsAsync(bool enabled)
        {
            if (_enableThirdPartyPlugins == enabled) return;

            _enableThirdPartyPlugins = enabled;
            await PersistToDiskAsync();
        }

        public bool IsAdminPasswordSet() => !string.IsNullOrWhiteSpace(ActivePassword);

        public bool VerifyAdminPassword(string plaintext)
        {
            var active = ActivePassword;
            if (string.IsNullOrEmpty(active)) return false;
            return PasswordHash.FixedTimeEquals(plaintext ?? string.Empty, active);
        }

        public async ValueTask SetAdminPasswordAsync(string plaintext)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

            _persistedPassword = plaintext;
            await PersistToDiskAsync();
        }

        private string ActivePassword =>
            !string.IsNullOrEmpty(_persistedPassword) ? _persistedPassword! : _defaultPassword;

        private void LoadFromDisk()
        {
            if (!File.Exists(_statePath))
            {
                _logger.LogInformation(
                    "No admin settings file at [{Path}]; using defaults (Third-party plugins disabled).",
                    _statePath);
                return;
            }

            try
            {
                using var stream = new FileStream(
                    _statePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                var doc = JsonSerializer.Deserialize<PersistedSettings>(stream, JsonOptions);

                _enableThirdPartyPlugins = doc?.EnableThirdPartyPlugins ?? false;
                _persistedPassword = string.IsNullOrEmpty(doc?.Password) ? null : doc.Password;

                _logger.LogInformation(
                    "Loaded admin settings from [{Path}]: EnableThirdPartyPlugins={Enabled}, PasswordSet={PasswordSet}.",
                    _statePath,
                    _enableThirdPartyPlugins,
                    _persistedPassword is not null);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to read admin settings from [{Path}]; using defaults.",
                    _statePath);
            }
        }

        private async Task PersistToDiskAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                var directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var payload = new PersistedSettings(_enableThirdPartyPlugins, _persistedPassword);

                var tempPath = _statePath + ".tmp";
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, payload, JsonOptions);
                }

                File.Move(tempPath, _statePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist admin settings to [{Path}]. Changes will be lost on restart.",
                    _statePath);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private sealed record PersistedSettings(
            [property: JsonPropertyName("enableThirdPartyPlugins")] bool EnableThirdPartyPlugins,
            [property: JsonPropertyName("password")] string? Password);
    }

    /// <summary>
    /// Shared password-comparison helper. Uses SHA-256 + <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// so failing attempts don't leak length or prefix information via
    /// response timing.
    /// </summary>
    internal static class PasswordHash
    {
        public static bool FixedTimeEquals(string left, string right)
        {
            var l = SHA256.HashData(Encoding.UTF8.GetBytes(left));
            var r = SHA256.HashData(Encoding.UTF8.GetBytes(right));
            return CryptographicOperations.FixedTimeEquals(l, r);
        }
    }
}
