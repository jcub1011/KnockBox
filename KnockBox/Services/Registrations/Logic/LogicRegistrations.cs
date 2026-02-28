using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Navigation.Games.DiceSimulator;
using KnockBox.Services.Navigation.Games.CardCounter;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();
            services.AddSingleton<IRandomNumberService, RandomNumberService>();

            // Register game engines
            services.AddSingleton<DiceSimulatorGameEngine>();
            services.AddSingleton<CardCounterGameEngine>();

            return services;
        }
    }
}
