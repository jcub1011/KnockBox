using KnockBox.Codeword.Components;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Codeword
{
    public class CodewordModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(CodewordModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<CodewordGameEngine>();

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<CodewordHeader>(0);
            builder.CloseComponent();
        };
    }
}
