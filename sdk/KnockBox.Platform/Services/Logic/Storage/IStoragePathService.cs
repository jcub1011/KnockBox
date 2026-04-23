namespace KnockBox.Platform.Storage;

public interface IStoragePathService
{
    string GetAdminDirectory();
    string GetLogDirectory();
    string GetFirstPartyPluginsDirectory();
    string GetExternalPluginsDirectory();

    /// <summary>
    /// Per-plugin data directory used as the root for
    /// <c>IPluginStorage</c>. Always returns the same path for a given
    /// <paramref name="routeIdentifier"/>. The directory is created lazily
    /// by <c>IPluginStorage</c> on first write.
    /// </summary>
    string GetPluginDataDirectory(string routeIdentifier);
}
