using KnockBox.Core.Plugins;
using KnockBox.DiceSimulator.Components;
using KnockBox.DiceSimulator.Services.Logic.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DiceSimulator
{
    public class DiceSimulatorModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(DiceSimulatorModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<DiceSimulatorGameEngine>();

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<DiceSimulatorHeader>(0);
            builder.CloseComponent();
        };
    }
}
