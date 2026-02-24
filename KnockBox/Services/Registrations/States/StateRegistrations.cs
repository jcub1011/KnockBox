using KnockBox.Services.State.Games.DiceSimulator;
using KnockBox.Services.State.Games.Lobbies;
using KnockBox.Services.State.Games.SplitTheDeck;

namespace KnockBox.Services.Registrations.States
{
    public static class StateRegistrations
    {
        public static IServiceCollection RegisterStateServices(this IServiceCollection services)
        {
            // Add states
            services.AddSingleton(typeof(IGameLobbyService<>), typeof(BaseGameLobbyService<>));
            services.AddTransient<DiceSimulatorLobby>();
            services.AddTransient<SplitTheDeckLobby>();
            return services;
        }
    }
}
