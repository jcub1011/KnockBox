using KnockBox.Core.Services.Browser;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Platform.Games;
using KnockBox.Services.Browser;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Shared;
using KnockBox.Platform.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Services.State.Users;
using KnockBox.Services.State.PlayLog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            // LobbyService is a singleton that also participates in the hosted-service
            // lifecycle so it can dispose every open lobby on ApplicationStopping. Register
            // the concrete type once and bridge both the ILobbyService and IHostedService
            // identities to it — otherwise StopAsync would run against a *second* instance
            // with an empty _lobbies.
            services.AddSingleton<LobbyService>();
            services.AddSingleton<ILobbyService>(sp => sp.GetRequiredService<LobbyService>());
            services.AddHostedService(sp => sp.GetRequiredService<LobbyService>());

            // Session service registrations
            services.AddSingleton<ISessionServiceProvider, SessionServiceProvider>();
            services.AddScoped<ISessionTokenProvider, SessionTokenProvider>();
            services.AddScoped<IUserService, UserService>();

            // Per-circuit browser-persisted history of games the user has played.
            // Scoped because it depends on the scoped ILocalStorageService.
            services.AddScoped<IPlayLogService, PlayLogService>();

            // GameSessionState is the long-lived session holder cached per user id by
            // ISessionServiceProvider. It must be Transient so the provider creates a fresh
            // instance on first access and caches it internally (not inside the DI scope).
            services.AddTransient<GameSessionState>();

            // GameSessionService is the per-circuit proxy that forwards session operations to
            // the user-id-backed GameSessionState, keeping navigation logic circuit-local.
            services.AddScoped<IGameSessionService, GameSessionService>();

            // Read-only observer attach for screen-shareable display views — looks up
            // the room state by route + obfuscated code without registering a user.
            services.AddSingleton<IGameRoomObserver, GameRoomObserver>();

            // Wake-lock service is per-circuit so each connected user manages their own
            // browser-side Screen Wake Lock independently.
            services.AddScoped<IWakeLockService, WakeLockService>();

            return services;
        }
    }
}
