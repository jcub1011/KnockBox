using KnockBox.Services.State.Games.Lobbies;

namespace KnockBox.Services.Registrations.States
{
    public static class StateRegistrations
    {
        public static IServiceCollection RegisterStateServices(this IServiceCollection services)
        {
            // Add states
            services.AddSingleton(typeof(IGameLobbyService<>), typeof(BaseGameLobbyService<>));
            services.AddTransient(typeof(GameLobby<>));

            return services;
        }
    }
}
