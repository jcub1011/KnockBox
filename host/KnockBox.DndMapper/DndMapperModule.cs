using KnockBox.Core.Plugins;
using KnockBox.DndMapper.Components;
using KnockBox.DndMapper.Services;
using KnockBox.DndMapper.Services.Library;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.Storage;
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
            registration.AddScoped<DndMapperStorage, DndMapperStorage>();
            registration.AddScoped<TokenFocusService, TokenFocusService>();
            registration.AddScoped<IFogPaintContext, FogPaintContext>();
            registration.AddScoped<IDiceAnimationTracker, DiceAnimationTracker>();
        }

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<DndMapperHeader>(0);
            builder.CloseComponent();
        };
    }
}
