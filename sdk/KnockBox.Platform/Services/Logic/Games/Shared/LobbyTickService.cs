using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnockBox.Services.Logic.Games.Shared
{
    /// <summary>
    /// Server-owned game clock. Replaces the old per-host Blazor circuit tick
    /// (<c>LobbyPageBase.TryGetHostTick</c>): in the WASM model the host has no circuit,
    /// so the server must drive time-based state transitions itself.
    /// <para>
    /// On a fixed cadence it walks every open lobby and, when the lobby's engine opts
    /// into ticking (<see cref="IServerTickHandler"/>), calls <c>Tick</c>. The engine
    /// mutates through its own state lock; the resulting state-change notification is
    /// turned into a per-recipient re-projection by the per-lobby
    /// <c>GameViewCoordinator</c> subscriber — so this service issues no broadcasts.
    /// </para>
    /// </summary>
    internal sealed class LobbyTickService : IHostedService
    {
        // Run at ~4 Hz. Game timeouts are second-resolution, so there is no need to
        // walk every open lobby at the TickService's full 20 Hz.
        private const int TickInterval = 5; // every 5th tick of the 20 TPS loop → 4 Hz

        private readonly LobbyService _lobbyService;
        private readonly ITickService _tickService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LobbyTickService> _logger;
        private IDisposable? _subscription;

        public LobbyTickService(
            LobbyService lobbyService,
            ITickService tickService,
            IServiceProvider serviceProvider,
            ILogger<LobbyTickService> logger)
        {
            _lobbyService = lobbyService;
            _tickService = tickService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var result = _tickService.RegisterTickCallback(
                () => TickOnce(DateTimeOffset.UtcNow), TickInterval);

            if (result.TryGetSuccess(out var sub))
                _subscription = sub;
            else
                _logger.LogError("Failed to register lobby tick callback: {Error}", result.Error);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _subscription?.Dispose();
            _subscription = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Walks every open lobby once and ticks the tickable ones. Internal so tests
        /// can drive a deterministic tick without the background timing loop.
        /// </summary>
        internal void TickOnce(DateTimeOffset now)
        {
            foreach (var registration in _lobbyService.GetOpenLobbies())
            {
                if (registration.State.IsDisposed)
                    continue;

                var engine = _serviceProvider.GetKeyedService<AbstractGameEngine>(registration.RouteIdentifier);
                if (engine is not IServerTickHandler handler)
                    continue;

                try
                {
                    handler.Tick(registration.State, now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error ticking lobby [{Uri}] for route [{Route}].",
                        registration.Uri,
                        registration.RouteIdentifier);
                }
            }
        }
    }
}
