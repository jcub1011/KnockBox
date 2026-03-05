using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Logic.Games.DiceSimulator;
using KnockBox.Services.Logic.Games.Engines.Shared;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            // Register game engines (also registered as AbstractGameEngine for discovery)
            services.AddSingleton<DiceSimulatorGameEngine>();
            services.AddSingleton<AbstractGameEngine>(sp => sp.GetRequiredService<DiceSimulatorGameEngine>());
            services.AddSingleton<CardCounterGameEngine>();
            services.AddSingleton<AbstractGameEngine>(sp => sp.GetRequiredService<CardCounterGameEngine>());

            return services;
        }
    }
}
