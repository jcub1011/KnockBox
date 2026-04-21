using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Spardle;

public class SpardleModule : IGameModule
{
    public string Name => "Spar-dle";
    public string Description => "A fast-paced competitive word guessing game.";
    public string RouteIdentifier => "spardle";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Services.WordListService>();
        services.AddGameEngine<SpardleEngine>(RouteIdentifier);
    }

    public RenderFragment GetButtonContent() => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "game-tile");
        builder.AddContent(2, "Spar-dle");
        builder.CloseElement();
    };
}
