namespace KnockBox.Platform.Storage;

/// <summary>
/// Platform-default <see cref="IStoragePathService"/>. Anchors persisted state
/// at <c>{KNOCKBOX_DATA_ROOT}</c> when that environment variable is set;
/// otherwise falls back to <see cref="AppContext.BaseDirectory"/><c>/data</c>.
/// First-party plugins always live at <see cref="AppContext.BaseDirectory"/>
/// <c>/games</c> because they ship inside the deployed artifact and are never
/// persisted across updates. Registered via <c>TryAddSingleton</c> inside
/// <c>AddKnockBoxPlatform</c> so a host can override any path by registering
/// its own implementation first.
/// </summary>
public sealed class DefaultStoragePathService : IStoragePathService
{
    private readonly string _dataRoot;

    public DefaultStoragePathService()
    {
        _dataRoot = ResolveDataRoot(Environment.GetEnvironmentVariable("KNOCKBOX_DATA_ROOT"));
    }

    public string GetAdminDirectory() =>
        Path.Combine(_dataRoot, "admin");

    public string GetLogDirectory() =>
        Path.Combine(_dataRoot, "logs");

    public string GetFirstPartyPluginsDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "games");

    public string GetExternalPluginsDirectory() =>
        Path.Combine(_dataRoot, "games");

    public string GetPluginDataDirectory(string routeIdentifier) =>
        Path.Combine(_dataRoot, "plugins", routeIdentifier);

    // Pure helper for testability — production passes the live env var, tests
    // pass a fixture string. Resolved once per process: an operator who
    // changes KNOCKBOX_DATA_ROOT mid-run would only confuse Serilog (which
    // captures its file path at startup) and the file-lock semantics inside
    // AdminSettingsService.
    internal static string ResolveDataRoot(string? envValue)
    {
        if (string.IsNullOrWhiteSpace(envValue))
            return Path.Combine(AppContext.BaseDirectory, "data");

        // Trim trailing separators and normalise to an absolute path so a
        // relative override survives a working-directory change.
        return Path.GetFullPath(envValue.TrimEnd('/', '\\'));
    }
}
