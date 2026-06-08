using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Client.Hub;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Platform.Hubs;

/// <summary>
/// Owns the single per-lobby subscription that replaces Blazor Server's
/// per-circuit <c>StateChangedEventManager</c> fan-out. On each state change it
/// projects a per-recipient view (under the read lock, for a consistent
/// snapshot) and pushes each projection to the right connection (outside the
/// lock, to keep the write-stall short).
/// </summary>
public sealed class GameViewCoordinator(
    IHubContext<GameHub, IGameClient> hubContext,
    GameConnectionRegistry registry,
    IServiceProvider serviceProvider,
    ILogger<GameViewCoordinator> logger)
{
    private sealed class LobbySubscription
    {
        public required string RouteIdentifier { get; init; }
        public IDisposable? StateChangedSub { get; set; }
        public IDisposable? StateDisposedSub { get; set; }
        public long Version;
    }

    private readonly ConcurrentDictionary<string, LobbySubscription> _subs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Idempotently installs the one per-lobby subscriber. Called when the hub
    /// sees the first connection for a lobby.
    /// </summary>
    public void EnsureSubscribed(LobbyRegistration reg)
    {
        _subs.GetOrAdd(reg.Uri, _ =>
        {
            var sub = new LobbySubscription { RouteIdentifier = reg.RouteIdentifier };
            // Notify fires OUTSIDE the execute lock (the load-bearing invariant),
            // so it is safe to take a read lock inside the handler.
            sub.StateChangedSub = reg.State.StateChangedEventManager.Subscribe(() => FanOutAsync(reg, sub));
            sub.StateDisposedSub = reg.State.SubscribeStateDisposed(() => RemoveSubscription(reg.Uri));
            logger.LogDebug("Installed view subscription for lobby [{Uri}].", reg.Uri);
            return sub;
        });
    }

    /// <summary>Projects and sends the current view to a single connection (e.g. on join).</summary>
    public async Task SendInitialAsync(LobbyRegistration reg, string connectionId, Guid recipientId)
    {
        if (!TryGetProjector(reg.RouteIdentifier, out var projector))
            return;

        var sub = _subs.TryGetValue(reg.Uri, out var s) ? s : null;
        var version = sub is not null ? Interlocked.Increment(ref sub.Version) : 0;

        object? view = null;
        reg.State.WithExclusiveRead(() => view = projector.ProjectFor(reg.State, recipientId));

        await hubContext.Clients.Client(connectionId)
            .ReceiveView(reg.RouteIdentifier, version, Serialize(view));
    }

    private async ValueTask FanOutAsync(LobbyRegistration reg, LobbySubscription sub)
    {
        var connections = registry.GetConnections(reg.Uri);
        if (connections.Count == 0)
            return;

        if (!TryGetProjector(reg.RouteIdentifier, out var projector))
            return;

        var version = Interlocked.Increment(ref sub.Version);

        // Project into cheap snapshot DTOs UNDER the read lock for a consistent
        // snapshot; serialize + send AFTER releasing it.
        var projected = new List<(string ConnectionId, object? View)>(connections.Count);
        reg.State.WithExclusiveRead(() =>
        {
            foreach (var (connectionId, userId) in connections)
                projected.Add((connectionId, projector.ProjectFor(reg.State, userId)));
        });

        foreach (var (connectionId, view) in projected)
        {
            try
            {
                await hubContext.Clients.Client(connectionId)
                    .ReceiveView(reg.RouteIdentifier, version, Serialize(view));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push projection to connection [{ConnectionId}].", connectionId);
            }
        }
    }

    /// <summary>Disposes a lobby's subscription once its last connection has left.</summary>
    public void RemoveSubscription(string lobbyUri)
    {
        if (_subs.TryRemove(lobbyUri, out var sub))
        {
            sub.StateChangedSub?.Dispose();
            sub.StateDisposedSub?.Dispose();
            logger.LogDebug("Removed view subscription for lobby [{Uri}].", lobbyUri);
        }
    }

    private bool TryGetProjector(string routeIdentifier, out IGameStateProjector projector)
    {
        var engine = serviceProvider.GetKeyedService<AbstractGameEngine>(routeIdentifier);
        if (engine is IGameStateProjector p)
        {
            projector = p;
            return true;
        }
        logger.LogError(
            "Game [{Route}] does not implement IGameStateProjector; cannot project state to clients.",
            routeIdentifier);
        projector = null!;
        return false;
    }

    // Reflection-based serialization: third-party view DTOs have no source-gen
    // context. Server-side reflection JSON is unaffected by trimming (only the
    // WASM client is trimmed). Enums are written as strings — the sane,
    // version-tolerant default for cross-assembly wire DTOs.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static string Serialize(object? view)
        => view is null ? "null" : JsonSerializer.Serialize(view, view.GetType(), SerializerOptions);
}
