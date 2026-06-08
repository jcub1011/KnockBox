using KnockBox.Core.Plugins;

namespace KnockBox.Core.Services.Storage.ClientStorage
{
    /// <summary>
    /// Base for a plugin's route-scoped client-storage service. Wraps the shared
    /// <see cref="ILocalStorageService"/> in a <see cref="ScopedClientStorageService"/>
    /// so every key is namespaced under the plugin's
    /// <see cref="IPluginManifest.RouteIdentifier"/> and can't collide with the
    /// host or another plugin.
    /// <para>
    /// Each plugin derives a one-line concrete type (e.g. <c>AlphaChainStorage</c>)
    /// so it gets a distinct DI registration; components inject that concrete type
    /// and read browser storage through <see cref="Local"/> rather than touching
    /// the raw <see cref="ILocalStorageService"/>.
    /// </para>
    /// </summary>
    public abstract class PluginClientStorage
    {
        protected PluginClientStorage(IPluginContext context, ILocalStorageService localStorage)
        {
            ArgumentNullException.ThrowIfNull(context);
            Local = new ScopedClientStorageService(localStorage, context.Manifest.RouteIdentifier);
        }

        /// <summary>Route-scoped browser <c>localStorage</c>.</summary>
        public IClientStorageService Local { get; }
    }
}
