using KnockBox.CardCounter.Components;
using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;

namespace KnockBox.CardCounter
{
    public class CardCounterModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(CardCounterModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<CardCounterGameEngine>();

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<CardCounterTile>(0);
            builder.CloseComponent();
        };

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<CardCounterHeader>(0);
            builder.CloseComponent();
        };
    }
}
