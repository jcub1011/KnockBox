using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.DrawnToDress.Services.Storage
{
    /// <summary>
    /// Route-scoped client storage for Drawn To Dress. See
    /// <see cref="PluginClientStorage"/> for the namespacing behavior.
    /// </summary>
    public sealed class DrawnToDressStorage(IPluginContext context, ILocalStorageService localStorage)
        : PluginClientStorage(context, localStorage);
}
