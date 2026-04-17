namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Service for managing global administrative settings.
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
