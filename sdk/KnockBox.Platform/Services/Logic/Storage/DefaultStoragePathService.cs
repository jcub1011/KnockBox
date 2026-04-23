namespace KnockBox.Platform.Storage;

/// <summary>
/// Platform-default <see cref="IStoragePathService"/>. Anchors everything at
/// <see cref="AppContext.BaseDirectory"/> using the same layout the production
/// host uses. Registered via <c>TryAddSingleton</c> inside
/// <c>AddKnockBoxPlatform</c> so a host can override any path by registering
/// its own implementation first.
/// </summary>
internal sealed class DefaultStoragePathService : IStoragePathService
{
    private const string DataRoot = "data";

    public string GetAdminDirectory() =>
        Path.Combine(AppContext.BaseDirectory, DataRoot, "admin");

    public string GetLogDirectory() =>
        Path.Combine(AppContext.BaseDirectory, DataRoot, "logs");

    public string GetFirstPartyPluginsDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "games");

    public string GetExternalPluginsDirectory() =>
        Path.Combine(AppContext.BaseDirectory, DataRoot, "games");

    public string GetPluginDataDirectory(string routeIdentifier) =>
        Path.Combine(AppContext.BaseDirectory, DataRoot, "plugins", routeIdentifier);
}
