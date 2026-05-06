using KnockBox.Platform.Storage;

namespace KnockBox.Services.Logic.Storage
{
    /// <summary>
    /// Host implementation of <see cref="IStoragePathService"/>. Mirrors the
    /// platform default: persisted state goes under <c>{KNOCKBOX_DATA_ROOT}</c>
    /// when that environment variable is set, otherwise
    /// <see cref="AppContext.BaseDirectory"/><c>/data</c>. First-party plugins
    /// always live at <see cref="AppContext.BaseDirectory"/><c>/games</c>
    /// because they ship inside the deployed artifact and must not move with
    /// the data root.
    /// </summary>
    internal sealed class StoragePathService : IStoragePathService
    {
        private readonly string _dataRoot;

        public StoragePathService()
        {
            _dataRoot = ResolveDataRoot(Environment.GetEnvironmentVariable("KNOCKBOX_DATA_ROOT"));
        }

        public string GetAdminDirectory() =>
            Path.Combine(_dataRoot, "admin");

        public string GetLogDirectory() =>
            Path.Combine(_dataRoot, "logs");

        public string GetFirstPartyPluginsDirectory() =>
            Path.Combine(AppContext.BaseDirectory, "games");

        public string GetExternalPluginsDirectory() =>
            Path.Combine(_dataRoot, "games");

        public string GetPluginDataDirectory(string routeIdentifier) =>
            Path.Combine(_dataRoot, "plugins", routeIdentifier);

        internal static string ResolveDataRoot(string? envValue)
        {
            if (string.IsNullOrWhiteSpace(envValue))
                return Path.Combine(AppContext.BaseDirectory, "data");

            return Path.GetFullPath(envValue.TrimEnd('/', '\\'));
        }
    }
}
