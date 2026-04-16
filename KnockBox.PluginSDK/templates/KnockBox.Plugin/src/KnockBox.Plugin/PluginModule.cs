using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using KnockBox.Plugin.Components;
using KnockBox.Plugin.Services.Logic;

namespace KnockBox.Plugin
{
    public class PluginModule : IGameModule
    {
        public string Name => "My New Game";
        public string Description => "A description of the game.";
        public string RouteIdentifier => "my-new-game";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<GameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<GameTile>(0);
            builder.CloseComponent();
        };
    }
}
