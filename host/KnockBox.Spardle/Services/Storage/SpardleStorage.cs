using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.Spardle.Services.Storage;

/// <summary>
/// Route-scoped client storage for Spardle. See
/// <see cref="PluginClientStorage"/> for the namespacing behavior.
/// </summary>
public sealed class SpardleStorage(IPluginContext context, ILocalStorageService localStorage)
    : PluginClientStorage(context, localStorage);
