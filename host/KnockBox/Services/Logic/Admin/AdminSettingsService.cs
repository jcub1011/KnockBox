using KnockBox.Platform.Storage;
using KnockBox.Services.Logic.Admin;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// File-backed implementation of <see cref="IAdminSettingsService"/>.
    /// </summary>
    internal sealed class AdminSettingsService : IAdminSettingsService
    {
        private const int Iterations = 100_000;
        private const int SaltSize = 16; // 128 bits
        private const int KeySize = 32;  // 256 bits
        private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly string _statePath;
        private readonly ILogger _logger;
        private readonly AdminOptions _options;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private bool _enableThirdPartyPlugins;
        private string? _passwordHash;

        public AdminSettingsService(
            IStoragePathService storagePath,
            IOptions<AdminOptions> options,
            ILogger<AdminSettingsService> logger)
        {
            _logger = logger;
            _options = options.Value;
            _statePath = Path.Combine(storagePath.GetAdminDirectory(), options.Value.SettingsPath);

            LoadFromDisk();
        }

        public bool GetEnableThirdPartyPlugins() => _enableThirdPartyPlugins;

        public async ValueTask SetEnableThirdPartyPluginsAsync(bool enabled)
        {
            if (_enableThirdPartyPlugins == enabled) return;

            _enableThirdPartyPlugins = enabled;
            await PersistToDiskAsync();
        }

        public bool IsPasswordDefault() => string.IsNullOrWhiteSpace(_passwordHash);

        public bool VerifyPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(_passwordHash))
            {
                // Fallback to bootstrap password in appsettings.json
                return FixedTimeEquals(password, _options.Password);
            }

            try
            {
                var parts = _passwordHash.Split(':');
                if (parts.Length != 4 || parts[0] != "v1") return false;

                var iterations = int.Parse(parts[1]);
                var salt = Convert.FromBase64String(parts[2]);
                var hash = Convert.FromBase64String(parts[3]);

                var inputHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    iterations,
                    HashAlgorithm,
                    hash.Length);

                return CryptographicOperations.FixedTimeEquals(inputHash, hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify admin password hash format.");
                return false;
            }
        }

        public async ValueTask UpdatePasswordAsync(string newPassword)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                newPassword,
                salt,
                Iterations,
                HashAlgorithm,
                KeySize);

            _passwordHash = $"v1:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
            await PersistToDiskAsync();
        }

        private void LoadFromDisk()
        {
            if (!File.Exists(_statePath))
            {
                _logger.LogInformation(
                    "No admin settings file at [{Path}]; using defaults.",
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
                _passwordHash = doc?.PasswordHash;

                _logger.LogInformation(
                    "Loaded admin settings from [{Path}]: EnableThirdPartyPlugins={Value}, CustomPassword={HasPassword}.",
                    _statePath,
                    _enableThirdPartyPlugins,
                    !string.IsNullOrWhiteSpace(_passwordHash));
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

                var payload = new PersistedSettings(_enableThirdPartyPlugins, _passwordHash);

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

        private static bool FixedTimeEquals(string left, string right)
        {
            var l = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(left));
            var r = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(right));
            return CryptographicOperations.FixedTimeEquals(l, r);
        }

        private sealed record PersistedSettings(
            [property: JsonPropertyName("enableThirdPartyPlugins")] bool EnableThirdPartyPlugins,
            [property: JsonPropertyName("passwordHash")] string? PasswordHash = null);
    }
}
