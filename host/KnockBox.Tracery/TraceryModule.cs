using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.Storage;
using KnockBox.Core.Plugins;

namespace KnockBox.Tracery
{
    public class TraceryModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(TraceryModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddGameEngine<TraceryGameEngine>();
            registration.AddScoped<TraceryStorage, TraceryStorage>();
        }

        // The game UI now lives in the WASM client (KnockBox.Tracery.Client); the server registers
        // only the engine. The hub resolves its IGameStateProjector / IGameCommandHandler off the
        // keyed AbstractGameEngine, and the WASM client renders the custom header — so there is no
        // server-side GetCustomHeader.
    }
}
