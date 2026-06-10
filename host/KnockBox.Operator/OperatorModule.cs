using KnockBox.Core.Plugins;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.Storage;

namespace KnockBox.Operator
{
    public class OperatorModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(OperatorModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddGameEngine<OperatorGameEngine>();
            registration.AddScoped<OperatorStorage, OperatorStorage>();
        }
    }
}
