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

            // Multiple CircuitHandler registrations under the same service type are intentional.
            // Blazor Server resolves all of them via IEnumerable<CircuitHandler> and invokes
            // each handler at every circuit lifecycle event — there is no conflict.
            services.AddScoped<CircuitHandler, LobbyCircuitHandler>();
            services.AddScoped<CircuitHandler, IDBackedCircuitHandler>();

            return services;
        }
    }
}
