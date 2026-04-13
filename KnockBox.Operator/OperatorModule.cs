using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Operator.Services.Logic.Games;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Operator
{
    public class OperatorModule : IGameModule
    {
        public string Name => "Operator";
        public string Description => "Decode and transmit the right signal.";
        public string RouteIdentifier => "operator";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddKeyedSingleton<AbstractGameEngine, OperatorGameEngine>(RouteIdentifier);
        }
    }
}
