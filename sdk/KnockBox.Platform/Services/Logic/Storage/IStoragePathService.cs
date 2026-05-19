namespace KnockBox.Platform.Storage;

public interface IStoragePathService
{
    /// <summary>
    /// Absolute path to the resolved data root — the parent of every other
    /// directory this service exposes. Equal to <c>{KNOCKBOX_DATA_ROOT}</c>
    /// when that environment variable is set, otherwise
    /// <see cref="AppContext.BaseDirectory"/><c>/data</c>. Resolved once at
    /// construction; later env-var mutations are not observed.
    /// </summary>
    string GetDataRoot();

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
