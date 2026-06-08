using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.LinkedList.Services.Storage
{
    /// <summary>
    /// Route-scoped client storage for Linked List. See
    /// <see cref="PluginClientStorage"/> for the namespacing behavior.
    /// </summary>
    public sealed class LinkedListStorage(IPluginContext context, ILocalStorageService localStorage)
        : PluginClientStorage(context, localStorage);
}
