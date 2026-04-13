using KnockBox.Core.Plugins;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
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
