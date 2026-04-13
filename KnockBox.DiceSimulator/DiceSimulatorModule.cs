using KnockBox.Core.Plugins;
using KnockBox.DiceSimulator.Services.Logic.Games;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.DiceSimulator
{
    public class DiceSimulatorModule : IGameModule
    {
        public string Name => "Dice Simulator";
        public string Description => "A physics based dice roller.";
        public string RouteIdentifier => "dice-simulator";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<DiceSimulatorGameEngine>(RouteIdentifier);
        }
    }
}
