using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.State;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KnockBox.Services.Registrations.States
{
    public static class StateRegistrations
    {
        public static IServiceCollection RegisterStateServices(this IServiceCollection services)
        {
            // Add states
            services.AddSingleton<ILobbyService, LobbyService>();
            services.AddSingleton<IIDBackedServiceProvider, IDBackedServiceProvider>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IGameSessionService, GameSessionService>();
            services.AddScoped<CircuitHandler, LobbyCircuitHandler>();
            services.AddScoped<CircuitHandler, IDBackedCircuitHandler>();

            return services;
        }
    }
}
