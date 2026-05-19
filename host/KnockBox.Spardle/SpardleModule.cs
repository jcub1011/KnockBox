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
        // IWordListService is provided by the KnockBox.WordService library
        // plugin (a sibling first-party plugin loaded before Spardle). Spardle
        // just consumes it via constructor injection in SpardleEngine.
        registration.AddGameEngine<SpardleEngine>();
    }

    public RenderFragment? GetCustomHeader() => builder =>
    {
        builder.OpenComponent<SpardleHeader>(0);
        builder.CloseComponent();
    };
}
