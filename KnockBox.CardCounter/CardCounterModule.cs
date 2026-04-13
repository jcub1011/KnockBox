using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.CardCounter.Services.Logic.Games;
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
            services.AddKeyedSingleton<AbstractGameEngine, CardCounterGameEngine>(RouteIdentifier);
        }
    }
}
