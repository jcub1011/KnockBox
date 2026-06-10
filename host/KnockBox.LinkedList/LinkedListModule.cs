using KnockBox.Core.Plugins;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.Storage;

namespace KnockBox.LinkedList
{
    public class LinkedListModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(LinkedListModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddSingleton<WordSource, WordSource>();
            registration.AddGameEngine<LinkedListGameEngine>();
            registration.AddScoped<LinkedListStorage, LinkedListStorage>();
        }

        // The game UI now lives in the WASM client (KnockBox.LinkedList.Client); the server registers
        // only the engine. The hub resolves its IGameStateProjector / IGameCommandHandler off the
        // keyed AbstractGameEngine, and the WASM client renders the custom header — so there is no
        // server-side GetCustomHeader.
    }
}
