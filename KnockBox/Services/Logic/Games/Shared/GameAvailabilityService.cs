using KnockBox.Admin;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnockBox.Services.Logic.Games.Shared
{
    /// <summary>
    /// File-backed <see cref="IGameAvailabilityService"/>. The persisted shape
    /// is a JSON object with a single <c>"disabled"</c> array of route
    /// identifiers: the common case (all games enabled) serialises to a tiny
    /// payload, and unknown routes default to enabled without any entry at all.
    /// Writes go through a <see cref="SemaphoreSlim"/> so we never tear the
    /// file; reads go against an in-memory <see cref="ConcurrentDictionary{TKey, TValue}"/>
    /// so the hot paths (Home page render, LobbyService.CreateLobbyAsync) are
    /// lock-free.
    /// </summary>
    internal sealed class GameAvailabilityService : IGameAvailabilityService
    {
        // Keyed by route identifier (case-insensitive). Value = IsEnabled.
        // Absence means "enabled" so new plugins don't need to be pre-registered.
        private readonly ConcurrentDictionary<string, bool> _state =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly string _statePath;
        private readonly ILogger<GameAvailabilityService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public event Action? Changed;

        public GameAvailabilityService(
            IOptions<AdminOptions> options,
            ILogger<GameAvailabilityService> logger)
        {
            _logger = logger;

            var configuredPath = options.Value.GameStatePath;
            _statePath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);

            LoadFromDisk();
        }

        public bool IsEnabled(string routeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(routeIdentifier)) return true;
            return !_state.TryGetValue(routeIdentifier, out var enabled) || enabled;
        }

        public void SetEnabled(string routeIdentifier, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(routeIdentifier))
                throw new ArgumentException("Route identifier must be non-empty.", nameof(routeIdentifier));

            _state[routeIdentifier] = enabled;
            PersistToDisk();
            Changed?.Invoke();
        }

        public IReadOnlyDictionary<string, bool> GetAll()
        {
            // Snapshot so callers can enumerate safely.
            return _state.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        private void LoadFromDisk()
        {
            if (!File.Exists(_statePath))
            {
                _logger.LogInformation(
                    "No admin game-state file at [{Path}]; starting with all games enabled.",
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
                var doc = JsonSerializer.Deserialize<PersistedState>(stream, JsonOptions);
                if (doc?.Disabled is null)
                    return;

                foreach (var route in doc.Disabled)
                {
                    if (string.IsNullOrWhiteSpace(route)) continue;
                    _state[route] = false;
                }

                _logger.LogInformation(
                    "Loaded admin game-state from [{Path}]: {Count} disabled route(s).",
                    _statePath,
                    doc.Disabled.Count);
            }
            catch (Exception ex)
            {
                // Corrupted file shouldn't prevent startup -- log loudly and
                // proceed as if nothing is disabled. The admin can toggle
                // again to produce a well-formed file.
                _logger.LogError(
                    ex,
                    "Failed to read admin game-state from [{Path}]; starting with all games enabled.",
                    _statePath);
            }
        }

        private void PersistToDisk()
        {
            _fileLock.Wait();
            try
            {
                var directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var disabled = _state
                    .Where(kvp => !kvp.Value)
                    .Select(kvp => kvp.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var payload = new PersistedState(disabled);

                var tempPath = _statePath + ".tmp";
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    JsonSerializer.Serialize(stream, payload, JsonOptions);
                }

                // Atomic replace: Move overwrites on Windows .NET via overwrite flag.
                File.Move(tempPath, _statePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist admin game-state to [{Path}]. In-memory toggle applied but change will be lost on restart.",
                    _statePath);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private sealed record PersistedState(
            [property: JsonPropertyName("disabled")] IReadOnlyList<string> Disabled);
    }
}
