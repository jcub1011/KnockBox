using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Logic.Games.DiceSimulator;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.ConsultTheCard;
using KnockBox.Services.Logic.Games.Operator;

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
            services.AddSingleton<DrawnToDressGameEngine>();
            services.AddSingleton<ConsultTheCardGameEngine>();
            services.AddSingleton<OperatorGameEngine>();

            return services;
        }
    }
}
