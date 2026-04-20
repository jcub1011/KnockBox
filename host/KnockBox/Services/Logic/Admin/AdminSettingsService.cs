using KnockBox.Platform.Storage;
using KnockBox.Services.Logic.Admin;
using Microsoft.Extensions.Options;
using System.Globalization;
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

        // volatile: readers (GetEnableThirdPartyPlugins, IsPasswordDefault,
        // VerifyPassword) run unlocked for low-latency admin traffic. Writers
        // mutate these fields inside _fileLock (see SetEnableThirdPartyPluginsAsync
        // / UpdatePasswordAsync), so the volatile modifier is what gives unlocked
        // readers memory-visibility of completed writes. Stale reads are tolerable
        // here — two consecutive reads may cross a write boundary, which is fine
        // for the admin UI's eventual-consistency semantics.
        private volatile bool _enableThirdPartyPlugins;
        private volatile string? _passwordHash;

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

        /// <summary>
        /// Reads only the <c>EnableThirdPartyPlugins</c> toggle from the persisted
        /// settings file (falling back to <c>.bak</c> if the primary file is
        /// missing or malformed). Used during host startup to decide which plugin
        /// directories to scan before the DI container — and therefore the
        /// <see cref="IAdminSettingsService"/> singleton — is built. Returns
        /// <see langword="false"/> when no settings file or backup is readable.
        /// </summary>
        public static bool ReadThirdPartyToggleFromDisk(
            IStoragePathService storagePath, AdminOptions options)
        {
            var path = Path.Combine(storagePath.GetAdminDirectory(), options.SettingsPath);
            return TryRead(path) ?? TryRead(path + ".bak") ?? false;

            static bool? TryRead(string filePath)
            {
                if (!File.Exists(filePath)) return null;
                try
                {
                    using var stream = File.OpenRead(filePath);
                    var doc = JsonSerializer.Deserialize<PersistedSettings>(stream, JsonOptions);
                    return doc?.EnableThirdPartyPlugins ?? false;
                }
                catch
                {
                    return null;
                }
            }
        }

        public async ValueTask SetEnableThirdPartyPluginsAsync(bool enabled)
        {
            await _fileLock.WaitAsync();
            try
            {
                if (_enableThirdPartyPlugins == enabled) return;

                _enableThirdPartyPlugins = enabled;
                await PersistLockedAsync();
            }
            finally
            {
                _fileLock.Release();
            }
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

                var iterations = int.Parse(parts[1], CultureInfo.InvariantCulture);
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

            var newHash = $"v1:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";

            await _fileLock.WaitAsync();
            try
            {
                _passwordHash = newHash;
                await PersistLockedAsync();
            }
            finally
            {
                _fileLock.Release();
            }
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
                ReadSettingsFile(_statePath);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Admin settings file at [{Path}] is corrupted. Attempting to load from backup.", _statePath);
                
                var backupPath = _statePath + ".bak";
                if (File.Exists(backupPath))
                {
                    try
                    {
                        ReadSettingsFile(backupPath);
                        _logger.LogInformation("Successfully recovered admin settings from backup at [{BackupPath}].", backupPath);
                        return;
                    }
                    catch (Exception backupEx)
                    {
                        _logger.LogError(backupEx, "Failed to read admin settings from backup at [{BackupPath}]. Both settings and backup are corrupted.", backupPath);
                        throw;
                    }
                }
                
                _logger.LogError("No backup file found at [{BackupPath}]. Admin settings are corrupted and unrecoverable.", backupPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to read admin settings from [{Path}]; using defaults.",
                    _statePath);
            }
        }

        private void ReadSettingsFile(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var doc = JsonSerializer.Deserialize<PersistedSettings>(stream, JsonOptions);
            
            _enableThirdPartyPlugins = doc?.EnableThirdPartyPlugins ?? false;
            _passwordHash = doc?.PasswordHash;

            _logger.LogInformation(
                "Loaded admin settings from [{Path}]: EnableThirdPartyPlugins={Value}, CustomPassword={HasPassword}.",
                path,
                _enableThirdPartyPlugins,
                !string.IsNullOrWhiteSpace(_passwordHash));
        }

        /// <summary>
        /// Persists the current in-memory field snapshot to disk. Assumes
        /// <see cref="_fileLock"/> is already held by the caller, so the field
        /// write that preceded this call is serialized with the file IO that
        /// commits it.
        /// </summary>
        private async Task PersistLockedAsync()
        {
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

                // Backup is best-effort: if this copy fails the main file already
                // holds the new data, and the backup simply lags one revision.
                var backupPath = _statePath + ".bak";
                try
                {
                    File.Copy(_statePath, backupPath, overwrite: true);
                }
                catch (Exception backupEx)
                {
                    _logger.LogWarning(
                        backupEx,
                        "Persisted admin settings to [{Path}] but failed to refresh backup at [{BackupPath}].",
                        _statePath,
                        backupPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist admin settings to [{Path}]. Changes will be lost on restart.",
                    _statePath);
            }
        }

        private static bool FixedTimeEquals(string? left, string? right)
        {
            var l = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(left ?? string.Empty));
            var r = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(right ?? string.Empty));
            return CryptographicOperations.FixedTimeEquals(l, r);
        }

        private sealed record PersistedSettings(
            [property: JsonPropertyName("enableThirdPartyPlugins")] bool EnableThirdPartyPlugins,
            [property: JsonPropertyName("passwordHash")] string? PasswordHash = null);
    }
}
