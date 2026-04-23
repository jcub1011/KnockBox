using KnockBox.Core.Plugins;
using KnockBox.Operator.Components;
using KnockBox.Operator.Services.Logic.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Operator
{
    public class OperatorModule : IGameModule
    {
        public IPluginManifest Manifest { get; } =
            PluginManifest.FromEmbeddedResourceOrThrow(typeof(OperatorModule).Assembly);

        public void RegisterServices(IPluginRegistration registration)
            => registration.AddGameEngine<OperatorGameEngine>();

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<OperatorTile>(0);
            builder.CloseComponent();
        };

        public RenderFragment? GetCustomHeader() => builder =>
        {
            builder.OpenComponent<OperatorHeader>(0);
            builder.CloseComponent();
        };
    }
}
