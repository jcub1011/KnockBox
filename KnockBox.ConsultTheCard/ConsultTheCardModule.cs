using KnockBox.ConsultTheCard.Components;
using KnockBox.ConsultTheCard.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.ConsultTheCard
{
    public class ConsultTheCardModule : IGameModule
    {
        public string Name => "Consult The Card";
        public string Description => "A social card-based fortune teller.";
        public string RouteIdentifier => "consult-the-card";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<ConsultTheCardGameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<ConsultTheCardTile>(0);
            builder.CloseComponent();
        };
    }
}
