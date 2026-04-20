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

        /// <summary>
        /// True when an active admin password is available — either persisted
        /// in the writable admin folder or supplied as a non-empty
        /// <c>Admin:Password</c> default in configuration.
        /// </summary>
        bool IsAdminPasswordSet();

        /// <summary>
        /// Constant-time check of a plaintext submission against the
        /// currently active password. Returns false when no password is set.
        /// </summary>
        bool VerifyAdminPassword(string plaintext);

        /// <summary>
        /// Persists a new admin password to the admin folder, overriding any
        /// default. The caller is responsible for authorizing the change —
        /// initial set is unauthenticated; rotations must come from an
        /// authenticated admin session.
        /// </summary>
        ValueTask SetAdminPasswordAsync(string plaintext);
    }
}
