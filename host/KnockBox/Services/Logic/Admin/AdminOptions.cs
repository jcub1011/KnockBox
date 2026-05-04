namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Strongly-typed binding for the <c>Admin</c> section of appsettings.json.
    /// The admin interface is hidden behind a dedicated port and guarded by a
    /// single shared login pulled from this section.
    /// </summary>
    /// <remarks>
    /// Lives in the host assembly (not the Platform SDK package) so credentials
    /// and admin paths never reach the plugin-facing surface. Marked public so
    /// the admin login component can accept it through DI.
    /// </remarks>
    public sealed class AdminOptions
    {
        public const string SectionName = "Admin";

        /// <summary>
        /// The port on which the admin UI is served. Kestrel binds this port in
        /// addition to the main URL, and port-filtering middleware confines
        /// <c>/admin/*</c> routes to it (and bars them everywhere else).
        /// </summary>
        public int Port { get; init; } = 8081;

        /// <summary>
        /// Admin username. Stored plaintext per project requirement. There is
        /// no UI to change this at runtime, so the default of <c>admin</c> is
        /// the effective identity unless an operator overrides it via
        /// configuration.
        /// </summary>
        public string Username { get; init; } = "admin";

        /// <summary>
        /// Default admin password supplied via configuration (appsettings or
        /// the <c>Admin__Password</c> env var). The <em>active</em> password
        /// is resolved by <see cref="IAdminSettingsService"/> — the persisted
        /// value in the admin folder wins over this default. Empty means no
        /// default, which triggers the first-run initialization flow.
        /// </summary>
        /// <remarks>
        /// This value is the fallback used by <c>AdminSettingsService.VerifyAdminPassword</c>
        /// only while no hashed password has been persisted. Once the admin initializes
        /// or changes the password via the UI, a PBKDF2 hash is written
        /// to the settings file and this appsettings value is no longer consulted
        /// — it may be rotated or cleared on the next restart without locking
        /// anyone out. It is still used again only if the settings file (and its
        /// <c>.bak</c> sibling) is manually deleted as an emergency reset.
        /// </remarks>
        public string Password { get; init; } = string.Empty;

        /// <summary>
        /// Filename of the persisted list of disabled game route identifiers.
        /// Resolved relative to <c>IStoragePathService.GetAdminDirectory()</c>.
        /// </summary>
        public string GameStatePath { get; init; } = "games-state.json";

        /// <summary>
        /// Filename of the persisted admin settings.
        /// Resolved relative to <c>IStoragePathService.GetAdminDirectory()</c>.
        /// </summary>
        public string SettingsPath { get; init; } = "settings.json";

        /// <summary>
        /// Directory where log files are stored. Resolved relative to
        /// <c>IStoragePathService.GetLogDirectory()</c>. In the default
        /// implementation, this option is ignored in favour of the service's
        /// hardcoded path.
        /// </summary>
        public string LogDirectory { get; init; } = "logs";
    }
}
