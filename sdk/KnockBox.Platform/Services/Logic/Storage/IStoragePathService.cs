namespace KnockBox.Platform.Storage;

public interface IStoragePathService
{
    string GetAdminDirectory();
    string GetLogDirectory();
    string GetFirstPartyPluginsDirectory();
    string GetExternalPluginsDirectory();
}
