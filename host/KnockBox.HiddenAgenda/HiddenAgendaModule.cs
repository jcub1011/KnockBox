using KnockBox.Core.Plugins;
using KnockBox.HiddenAgenda.Components;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda
{
    public class HiddenAgendaModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(HiddenAgendaModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<HiddenAgendaGameEngine>();

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<HiddenAgendaTile>(0);
            builder.CloseComponent();
        };

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<HiddenAgendaHeader>(0);
            builder.CloseComponent();
        };
    }
}
