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
            // Add memory cache
            services.AddMemoryCache();

            // Add states
            services.AddSingleton<TickService>();
            services.AddSingleton<ITickService>(sp => sp.GetRequiredService<TickService>());
            services.AddHostedService(sp => sp.GetRequiredService<TickService>());
            services.AddSingleton<ILobbyService, LobbyService>();

            // Session service registrations
            services.AddSingleton<ISessionServiceProvider, SessionServiceProvider>();
            services.AddScoped<ISessionTokenProvider, SessionTokenProvider>();
            services.AddScoped<IUserService, UserService>();

            // GameSessionState is the long-lived session holder cached per user id by
            // ISessionServiceProvider. It must be Transient so the provider creates a fresh
            // instance on first access and caches it internally (not inside the DI scope).
            services.AddTransient<GameSessionState>();

            // GameSessionService is the per-circuit proxy that forwards session operations to
            // the user-id-backed GameSessionState, keeping navigation logic circuit-local.
            services.AddScoped<IGameSessionService, GameSessionService>();

            return services;
        }
    }
}
