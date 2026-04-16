namespace KnockBox.Admin
{
    /// <summary>
    /// Strongly-typed binding for the <c>Admin</c> section of appsettings.json.
    /// The admin interface is hidden behind a dedicated port and guarded by a
    /// single shared login pulled from this section.
    /// </summary>
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
        /// Admin password. Stored plaintext per project requirement.
        /// </summary>
        public string Password { get; init; } = string.Empty;

        /// <summary>
        /// Relative (to <c>AppContext.BaseDirectory</c>) or absolute path at
        /// which the persisted list of disabled game route identifiers lives.
        /// </summary>
        public string GameStatePath { get; init; } = "admin/games-state.json";

        /// <summary>
        /// Relative (to <c>AppContext.BaseDirectory</c>) or absolute path to
        /// the directory where log files are stored.
        /// </summary>
        public string LogDirectory { get; init; } = "logs";
    }
}
