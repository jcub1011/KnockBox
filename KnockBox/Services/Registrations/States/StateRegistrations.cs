using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Registrations.States
{
    public static class StateRegistrations
    {
        public static IServiceCollection RegisterStateServices(this IServiceCollection services)
        {
            // Add states
            services.AddScoped<IUserService, UserService>();
            services.AddSingleton<ILobbyService, LobbyService>();
            return services;
        }
    }
}
