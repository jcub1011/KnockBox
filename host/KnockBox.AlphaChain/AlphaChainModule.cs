using KnockBox.AlphaChain.Components;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain
{
    public class AlphaChainModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(AlphaChainModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            // The deterministic scoring pipeline is a stateless singleton shared by every
            // Alpha Chain room; the engine resolves it from DI and forwards it onto the
            // per-game context.
            registration.AddSingleton<IScoreCalculator, ScoreCalculator>();
            registration.AddGameEngine<AlphaChainGameEngine>();
        }

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<AlphaChainHeader>(0);
            builder.CloseComponent();
        };
    }
}
