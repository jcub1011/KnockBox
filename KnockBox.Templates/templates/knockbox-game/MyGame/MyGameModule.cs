using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGame.Components;

namespace MyGame;

public class MyGameModule : IGameModule
{
    public string Name => "My Game";
    public string Description => "A KnockBox party game.";
    public string RouteIdentifier => "my-game";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddGameEngine<MyGameGameEngine>(RouteIdentifier);
    }

    public RenderFragment GetButtonContent() => builder =>
    {
        builder.OpenComponent<MyGameTile>(0);
        builder.CloseComponent();
    };
}
