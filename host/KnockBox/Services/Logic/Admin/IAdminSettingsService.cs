namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Service for managing global administrative settings. Lives in the host
    /// assembly (not the Platform SDK package) so plugins cannot reference or
    /// implement it; the host's Razor Pages and components that consume it
    /// are compiled against this host-local definition.
    /// </summary>
    public interface IAdminSettingsService
    {
        /// <summary>
        /// Gets whether third-party plugins are allowed to be loaded.
        /// Changes to this setting require a server restart.
        /// </summary>
        bool GetEnableThirdPartyPlugins();

        /// <summary>
        /// Sets whether third-party plugins should be allowed.
        /// </summary>
        ValueTask SetEnableThirdPartyPluginsAsync(bool enabled);
    }
}
