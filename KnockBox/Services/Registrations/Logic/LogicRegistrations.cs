using KnockBox.Services.Logic.Filtering;
using KnockBox.Services.Logic.Games.Lobbies;

namespace KnockBox.Services.Registrations.Logic
{
    public static class LogicRegistrations
    {
        public static IServiceCollection RegisterLogic(this IServiceCollection services)
        {
            services.AddSingleton<IProfanityFilter, ProfanityFilter>();
            services.AddSingleton<ILobbyCodeService, LobbyCodeService>();

            return services;
        }
    }
}
