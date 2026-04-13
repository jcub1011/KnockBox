using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.DrawnToDress.Services.Logic.Games;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.DrawnToDress
{
    public class DrawnToDressModule : IGameModule
    {
        public string Name => "Drawn To Dress";
        public string Description => "The drawing and dress-up game.";
        public string RouteIdentifier => "drawn-to-dress";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddKeyedSingleton<AbstractGameEngine, DrawnToDressGameEngine>(RouteIdentifier);
        }
    }
}
