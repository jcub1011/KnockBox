using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.Core.Plugins;

namespace KnockBox.CardCounter
{
    public class CardCounterModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(CardCounterModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            // The game UI now lives in the WASM client (KnockBox.CardCounter.Client). The
            // server registers only the engine; the hub resolves its IGameStateProjector /
            // IGameCommandHandler / IServerTickHandler off the keyed AbstractGameEngine.
            registration.AddGameEngine<CardCounterGameEngine>();
        }
    }
}
