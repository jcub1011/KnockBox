using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.DndMapper.Services.Storage
{
    /// <summary>
    /// Route-scoped client storage for DnD Mapper's host preferences (separate
    /// from the IndexedDB library, which is scoped in
    /// <see cref="Library.DndMapperLibraryService"/>). See
    /// <see cref="PluginClientStorage"/> for the namespacing behavior.
    /// </summary>
    public sealed class DndMapperStorage(IPluginContext context, ILocalStorageService localStorage)
        : PluginClientStorage(context, localStorage);
}
