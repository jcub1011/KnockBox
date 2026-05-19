using KnockBox.AlphaChain.Components;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain
{
    public class AlphaChainModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(AlphaChainModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<AlphaChainGameEngine>();

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<AlphaChainTile>(0);
            builder.CloseComponent();
        };

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<AlphaChainHeader>(0);
            builder.CloseComponent();
        };
    }
}
