using KnockBox.Core.Services.State.Shared;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.State;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KnockBox.Services.Registrations.States
{
    public static class StateRegistrations
    {
        public static IServiceCollection RegisterStateServices(this IServiceCollection services)
        {
            // Add states
            services.AddSingleton<TickService>();
            services.AddSingleton<ITickService>(sp => sp.GetRequiredService<TickService>());
            services.AddHostedService(sp => sp.GetRequiredService<TickService>());
            services.AddSingleton<ILobbyService, LobbyService>();
            services.AddSingleton<IIDBackedServiceProvider, IDBackedServiceProvider>();
            services.AddScoped<IUserService, UserService>();

            // GameSessionState is the long-lived session holder cached per user id by
            // IIDBackedServiceProvider. It must be Transient so the provider creates a fresh
            // instance on first access and caches it internally (not inside the DI scope).
            services.AddTransient<GameSessionState>();

            // GameSessionService is the per-circuit proxy that forwards session operations to
            // the user-id-backed GameSessionState, keeping navigation logic circuit-local.
            services.AddScoped<IGameSessionService, GameSessionService>();

            // IDBackedCircuitHandler notifies IIDBackedServiceProvider of circuit lifecycle
            // events so the per-user disposal grace period is managed correctly.
            services.AddScoped<CircuitHandler, IDBackedCircuitHandler>();

            return services;
        }
    }
}
