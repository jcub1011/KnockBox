using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.Operator.Services.Storage
{
    /// <summary>
    /// Route-scoped client storage for Operator. See
    /// <see cref="PluginClientStorage"/> for the namespacing behavior.
    /// </summary>
    public sealed class OperatorStorage(IPluginContext context, ILocalStorageService localStorage)
        : PluginClientStorage(context, localStorage);
}
