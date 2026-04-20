using KnockBox.Core.Plugins;
using KnockBox.HiddenAgenda.Components;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.HiddenAgenda
{
    public class HiddenAgendaModule : IGameModule
    {
        public string Name => "Hidden Agenda";
        public string Description => "Uncover the traitor among you.";
        public string RouteIdentifier => "hidden-agenda";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<HiddenAgendaGameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<HiddenAgendaTile>(0);
            builder.CloseComponent();
        };
    }
}
