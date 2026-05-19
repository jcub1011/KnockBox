using KnockBox.Core.Plugins;
using KnockBox.Spardle.Components;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Spardle;

public class SpardleModule : IGameModule
{
    public IPluginManifest Manifest { get; } =
        PluginManifest.FromEmbeddedResourceOrThrow(typeof(SpardleModule).Assembly);

    public void RegisterServices(IPluginRegistration registration)
    {
        registration.AddSingleton<Services.IWordListService, Services.WordListService>();
        registration.AddGameEngine<SpardleEngine>();
    }

    public RenderFragment? GetCustomHeader() => builder =>
    {
        builder.OpenComponent<SpardleHeader>(0);
        builder.CloseComponent();
    };
}
