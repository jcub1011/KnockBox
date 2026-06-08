using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.HiddenAgenda.Services.Storage
{
    /// <summary>
    /// Route-scoped client storage for Hidden Agenda. See
    /// <see cref="PluginClientStorage"/> for the namespacing behavior.
    /// </summary>
    public sealed class HiddenAgendaStorage(IPluginContext context, ILocalStorageService localStorage)
        : PluginClientStorage(context, localStorage);
}
