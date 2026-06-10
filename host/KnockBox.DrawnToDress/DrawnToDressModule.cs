using KnockBox.Core.Plugins;
using KnockBox.DrawnToDress.Components;
using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Storage;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress
{
    public class DrawnToDressModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(DrawnToDressModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddGameEngine<DrawnToDressGameEngine>();
            registration.AddScoped<DrawnToDressStorage, DrawnToDressStorage>();
        }

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<DrawnToDressHeader>(0);
            builder.CloseComponent();
        };
    }
}
