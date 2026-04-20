using KnockBox.Core.Plugins;
using KnockBox.DiceSimulator.Components;
using KnockBox.DiceSimulator.Services.Logic.Games;
using Microsoft.AspNetCore.Components;
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

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<DiceSimulatorTile>(0);
            builder.CloseComponent();
        };
    }
}
