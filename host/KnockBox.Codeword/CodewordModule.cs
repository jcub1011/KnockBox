using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Core.Plugins;

namespace KnockBox.Codeword
{
    public class CodewordModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(CodewordModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
        {
            registration.AddGameEngine<CodewordGameEngine>();
        }
    }
}
