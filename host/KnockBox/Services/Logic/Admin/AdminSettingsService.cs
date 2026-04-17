using KnockBox.Services.Logic.Admin;
using KnockBox.Services.Logic.Storage;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// File-backed implementation of <see cref="IAdminSettingsService"/>.
    /// </summary>
    internal sealed class AdminSettingsService : IAdminSettingsService
    {
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly string _statePath;
        private readonly ILogger _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private bool _enableThirdPartyPlugins;

        public AdminSettingsService(
            IStoragePathService storagePath,
            IOptions<AdminOptions> options,
            ILogger<AdminSettingsService> logger)
        {
            _logger = logger;
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

                _logger.LogInformation(
                    "Loaded admin settings from [{Path}]: EnableThirdPartyPlugins={Value}.",
                    _statePath,
                    _enableThirdPartyPlugins);
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

                var payload = new PersistedSettings(_enableThirdPartyPlugins);

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
            [property: JsonPropertyName("enableThirdPartyPlugins")] bool EnableThirdPartyPlugins);
    }
}
