using KnockBox.CardCounter.Components;
using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.Core.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.CardCounter
{
    public class CardCounterModule : IGameModule
    {
        public string Name => "Card Counter";
        public string Description => "High stakes blackjack style counting.";
        public string RouteIdentifier => "card-counter";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddGameEngine<CardCounterGameEngine>(RouteIdentifier);
        }

        public RenderFragment GetButtonContent() => builder =>
        {
            builder.OpenComponent<CardCounterTile>(0);
            builder.CloseComponent();
        };
    }
}
