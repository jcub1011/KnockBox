using KnockBox.Tracery.Components;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Tracery
{
    public class TraceryModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(TraceryModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<TraceryGameEngine>();

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<TraceryHeader>(0);
            builder.CloseComponent();
        };
    }
}
