using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Storage;
using KnockBox.Core.Plugins;

namespace KnockBox.AlphaChain
{
    public class AlphaChainModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(AlphaChainModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            // The sequential scoring driver and the modifier-card factory are stateless singletons
            // shared by every Alpha Chain room; the engine resolves them from DI and forwards them
            // onto the per-game context.
            registration.AddSingleton<IEngineEvaluator, EngineEvaluator>();
            registration.AddSingleton<IModifierCardFactory, ModifierCardFactory>();
            registration.AddGameEngine<AlphaChainGameEngine>();
            registration.AddScoped<AlphaChainStorage, AlphaChainStorage>();
        }

        // The game UI now lives in the WASM client (KnockBox.AlphaChain.Client); the server
        // registers only the engine. The hub resolves its IGameStateProjector /
        // IGameCommandHandler / IServerTickHandler off the keyed AbstractGameEngine, and the
        // WASM client renders the custom header — so there is no server-side GetCustomHeader.
    }
}
