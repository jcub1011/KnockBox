using KnockBox.Core.Plugins;
using KnockBox.DndMapper.Components;
using KnockBox.DndMapper.Services;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper
{
    public class DndMapperModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(DndMapperModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddGameEngine<DndMapperGameEngine>();
            registration.AddScoped<DndMapperLibraryService, DndMapperLibraryService>();
            registration.AddScoped<TokenFocusService, TokenFocusService>();
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<DndMapperTile>(0);
            builder.CloseComponent();
        };

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<DndMapperHeader>(0);
            builder.CloseComponent();
        };
    }
}
