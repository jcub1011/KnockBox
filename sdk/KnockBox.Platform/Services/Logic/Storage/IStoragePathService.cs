namespace KnockBox.Services.Logic.Storage;

public interface IStoragePathService
{
    string GetAdminDirectory();
    string GetLogDirectory();
    string GetFirstPartyPluginsDirectory();
    string GetExternalPluginsDirectory();
}
