using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.CardCounter.Services.Storage
{
    /// <summary>
    /// Route-scoped client storage for Card Counter. Wrapping the shared
    /// localStorage service in a <see cref="ScopedClientStorageService"/>
    /// namespaces every key under this plugin's route, so it can't collide with
    /// the host or another plugin. Components inject this concrete service
    /// instead of the raw <see cref="ILocalStorageService"/>.
    /// </summary>
    public sealed class CardCounterStorage
    {
        public CardCounterStorage(IPluginContext context, ILocalStorageService localStorage)
        {
            Local = new ScopedClientStorageService(localStorage, context.Manifest.RouteIdentifier);
        }

        /// <summary>Route-scoped browser <c>localStorage</c>.</summary>
        public IClientStorageService Local { get; }
    }
}
