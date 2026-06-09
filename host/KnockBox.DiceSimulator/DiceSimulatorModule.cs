using KnockBox.Core.Plugins;
using KnockBox.DiceSimulator.Services.Logic.Games;

namespace KnockBox.DiceSimulator
{
    public class DiceSimulatorModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(DiceSimulatorModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<DiceSimulatorGameEngine>();
    }
}
