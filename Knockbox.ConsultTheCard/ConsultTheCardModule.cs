using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.ConsultTheCard.Services.Logic.Games;
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
            services.AddKeyedSingleton<AbstractGameEngine, ConsultTheCardGameEngine>(RouteIdentifier);
        }
    }
}
