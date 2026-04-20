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
    /// Razor Pages (e.g. the login page model) can accept it through DI.
    /// </remarks>
    public sealed class AdminOptions
    {
        public const string SectionName = "Admin";

        /// <summary>
        /// The port on which the admin UI is served. Kestrel binds this port in
        /// addition to the main URL, and port-filtering middleware confines
        /// <c>/admin/*</c> routes to it (and bars them everywhere else).
        /// </summary>
        public int Port { get; init; } = 5277;

        /// <summary>
        /// Admin username. Stored plaintext per project requirement.
        /// </summary>
        public string Username { get; init; } = string.Empty;

        /// <summary>
        /// Bootstrap admin password. Stored plaintext per project requirement.
        /// </summary>
        /// <remarks>
        /// This value is the fallback used by <c>AdminSettingsService.VerifyPassword</c>
        /// only while no hashed password has been persisted. Once the admin changes
        /// the password via <c>/admin/changepassword</c>, a PBKDF2 hash is written
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
