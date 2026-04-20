namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Service for managing global administrative settings. Lives in the host
    /// assembly (not the Platform SDK package) so plugins cannot reference or
    /// implement it; the host's Razor Pages and components that consume it
    /// are compiled against this host-local definition.
    /// </summary>
    /// <remarks>
    /// Reader methods (<see cref="GetEnableThirdPartyPlugins"/>,
    /// <see cref="IsPasswordDefault"/>, <see cref="VerifyPassword"/>) are
    /// eventually consistent: they do not block on the persistence lock and may
    /// observe a value up to one in-flight write stale. Admin traffic is
    /// low-rate and single-operator, so this is intentional — do not rely on
    /// strict read-your-writes ordering from concurrent call sites.
    /// </remarks>
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

        /// <summary>
        /// Returns true if the admin is still using the default bootstrap
        /// password from configuration.
        /// </summary>
        bool IsPasswordDefault();

        /// <summary>
        /// Verifies the provided password against the persisted hash or the
        /// default configuration password.
        /// </summary>
        bool VerifyPassword(string password);

        /// <summary>
        /// Hashes and persists a new admin password.
        /// </summary>
        ValueTask UpdatePasswordAsync(string newPassword);
    }
}
