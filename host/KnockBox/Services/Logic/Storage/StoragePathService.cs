using KnockBox.Platform.Storage;

namespace KnockBox.Services.Logic.Storage
{
    internal sealed class StoragePathService : IStoragePathService
    {
        private const string DataRoot = "data";

        public string GetAdminDirectory() => 
            Path.Combine(AppContext.BaseDirectory, DataRoot, "admin");

        public string GetLogDirectory() => 
            Path.Combine(AppContext.BaseDirectory, DataRoot, "logs");

        public string GetFirstPartyPluginsDirectory() => 
            Path.Combine(AppContext.BaseDirectory, "games");

        public string GetExternalPluginsDirectory() => 
            Path.Combine(AppContext.BaseDirectory, DataRoot, "games");
    }
}
