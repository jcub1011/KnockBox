using KnockBox.Core.Client.Hub;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Platform.Hubs;

/// <summary>
/// The realtime transport for the WASM client. Replaces Blazor Server's implicit
/// per-circuit render-diff stream with explicit typed commands in / per-player
/// projections out.
/// <para>
/// Identity is established from the handshake, not trusted from a command
/// envelope: the connection carries the player's self-asserted id/name (KnockBox's
/// existing localStorage-backed player-identity model) as query string, resolved
/// once here. Command authorization (<c>caller.Id == state.Host.Id</c>) reuses the
/// sealed checks already in <see cref="AbstractGameEngine"/>.
/// </para>
/// </summary>
public sealed class GameHub(
    ILobbyService lobbyService,
    GameConnectionRegistry registry,
    GameViewCoordinator coordinator,
    IServiceProvider serviceProvider,
    ILogger<GameHub> logger) : Hub<IGameClient>
{
    /// <summary>
    /// Associates this connection with a lobby, joins its broadcast group,
    /// installs the per-lobby view subscriber (idempotent), and sends the caller
    /// an initial projection. Returns <c>null</c> on success or an error message.
    /// </summary>
    public async Task<string?> JoinRoom(string lobbyUri)
    {
        if (!TryResolveCaller(out var caller))
            return "Could not resolve caller identity from the connection.";

        if (!lobbyService.TryGetByUri(lobbyUri, out var registration))
            return $"Lobby [{lobbyUri}] not found.";

        // Install the per-lobby projection subscriber before any registration so
        // an existing member's view updates when this player joins.
        coordinator.EnsureSubscribed(registration);

        // The host is already in the roster; everyone else registers as a player.
        // The registration mutation fires StateChanged → existing members re-project.
        IDisposable? unregisterToken = null;
        if (caller.Id != registration.State.Host.Id)
        {
            var joinResult = registration.State.RegisterPlayer(caller);
            if (joinResult.TryGetSuccess(out var token))
                unregisterToken = token;
            // A failure here (lobby closed / already registered) is non-fatal for
            // the spike: the connection still receives projections as a spectator.
        }

        registry.Add(Context.ConnectionId, lobbyUri, caller.Id, caller.Name, unregisterToken);
        await Groups.AddToGroupAsync(Context.ConnectionId, lobbyUri);
        await coordinator.SendInitialAsync(registration, Context.ConnectionId, caller.Id);

        logger.LogDebug("Connection [{ConnectionId}] joined room [{Uri}] as [{UserId}].",
            Context.ConnectionId, lobbyUri, caller.Id);
        return null;
    }

    /// <summary>
    /// Creates a new lobby for <paramref name="routeIdentifier"/> with the caller as
    /// host and returns its URI. Lets the WASM spike page be fully self-driving
    /// (no dependency on the server-rendered home page).
    /// </summary>
    public async Task<string?> CreateRoom(string routeIdentifier)
    {
        if (!TryResolveCaller(out var caller))
            return null;

        var result = await lobbyService.CreateLobbyAsync(caller, routeIdentifier, Context.ConnectionAborted);
        return result.TryGetSuccess(out var registration) ? registration.Uri : null;
    }

    /// <summary>
    /// Dispatches a typed-over-the-wire command to the game engine. The resulting
    /// state mutation triggers the per-lobby fan-out automatically. Returns
    /// <c>null</c> on success or an error message.
    /// </summary>
    public async Task<string?> SubmitCommand(string lobbyUri, string command, string? payloadJson)
    {
        if (!TryResolveCaller(out var caller))
            return "Could not resolve caller identity from the connection.";

        if (!lobbyService.TryGetByUri(lobbyUri, out var registration))
            return $"Lobby [{lobbyUri}] not found.";

        var engine = serviceProvider.GetKeyedService<AbstractGameEngine>(registration.RouteIdentifier);
        if (engine is not IGameCommandHandler handler)
            return $"Game [{registration.RouteIdentifier}] does not accept hub commands.";

        var result = await handler.HandleCommandAsync(
            caller, registration.State, command, payloadJson, Context.ConnectionAborted);

        if (result.TryGetFailure(out var error))
        {
            logger.LogInformation("Command [{Command}] rejected for [{UserId}]: {Error}",
                command, caller.Id, error.InternalMessage);
            return error.PublicMessage;
        }
        return null;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (registry.TryRemove(Context.ConnectionId, out var lobbyUri, out var lobbyNowEmpty, out var unregisterToken))
        {
            // Unregister the player from the lobby state (fires PlayerUnregistered +
            // StateChanged → remaining members re-project with the updated roster).
            unregisterToken?.Dispose();
            if (lobbyNowEmpty)
                coordinator.RemoveSubscription(lobbyUri);
        }
        return base.OnDisconnectedAsync(exception);
    }

    private bool TryResolveCaller(out User caller)
    {
        caller = null!;
        var http = Context.GetHttpContext();
        if (http is null)
            return false;

        var rawId = http.Request.Query["userId"].ToString();
        var name = http.Request.Query["userName"].ToString();
        if (!Guid.TryParse(rawId, out var userId))
            return false;

        caller = UserFactory.Create(string.IsNullOrWhiteSpace(name) ? "Player" : name, userId);
        return true;
    }
}
