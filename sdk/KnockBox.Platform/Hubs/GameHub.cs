using KnockBox.Core.Client.Hub;
using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Platform.Games;
using KnockBox.Services.State.Games.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Platform.Hubs;

/// <summary>
/// The realtime transport for the WASM client. Replaces Blazor Server's implicit
/// per-circuit render-diff stream with explicit typed commands in / per-player
/// projections out.
/// <para>
/// Identity is established from the handshake, not trusted from a command
/// envelope: the connection presents a server-signed, per-tab identity token (via
/// the SignalR access-token mechanism) which is unprotected here to resolve the
/// caller's <see cref="User"/> and <see cref="SessionToken"/> without a Blazor
/// circuit. Command authorization (<c>caller.Id == state.Host.Id</c>) reuses the
/// sealed checks already in <see cref="AbstractGameEngine"/>.
/// </para>
/// <para>
/// The session lifecycle moves here from the circuit: <see cref="OnConnectedAsync"/>
/// acquires a session-service reference on the first connection for a token and
/// <see cref="OnDisconnectedAsync"/> releases it on the last, so reconnect within
/// the provider's grace window re-attaches the same session. Identity is now
/// <b>unified</b> with the still-circuit-based Blazor Server pages: their
/// <c>SessionTokenProvider</c> resolves the SAME signed per-tab token from
/// <c>sessionStorage</c> that this hub reads from the handshake, so a tab's circuit
/// and its hub connection produce the same <see cref="SessionToken"/> and co-own
/// the one cached <c>GameSessionState</c>. That cache is ref-counted with a 1-minute
/// eviction grace, so eviction waits for <b>both</b> the circuit and the hub to
/// release — a transition (Server page → WASM, or a reload) keeps the session warm
/// rather than evicting it. The hub tracks its per-lobby registration in
/// <see cref="GameConnectionRegistry"/>, not in <c>GameSessionState</c>, so the only
/// object the two paths share is the ref-counted cache entry.
/// </para>
/// </summary>
public sealed class GameHub(
    ILobbyService lobbyService,
    GameConnectionRegistry registry,
    GameViewCoordinator coordinator,
    ISessionServiceProvider sessionServiceProvider,
    ISessionIdentityTokenService identityTokens,
    IServiceProvider serviceProvider,
    ILogger<GameHub> logger) : Hub<IGameClient>
{
    private static readonly IDisposable NoOpLifecycleToken = new NoOpDisposable();

    /// <summary>
    /// Acquires the caller's session-service reference (keyed on the signed
    /// token) on the first connection for that token. Aborts the connection if
    /// identity can't be resolved from the handshake.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (!TryResolveCaller(out _, out var sessionToken))
        {
            Context.Abort();
            return;
        }

        registry.AddSession(sessionToken.Token, Context.ConnectionId, () =>
        {
            var result = sessionServiceProvider.GetService<GameSessionState>(sessionToken);
            return result.TryGetSuccess(out var registration)
                ? registration.LifecycleToken
                : NoOpLifecycleToken;
        });

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Associates this connection with a lobby, joins its broadcast group, and
    /// sends the caller an initial projection. The per-lobby view subscriber is
    /// installed at lobby creation (<c>LobbyService.CreateLobbyAsync</c>), not
    /// here, so it lives for the lobby's full lifetime. Returns <c>null</c> on
    /// success or an error message.
    /// </summary>
    public async Task<string?> JoinRoom(string lobbyUri)
    {
        if (!TryResolveCaller(out var caller, out _))
            return "Could not resolve caller identity from the connection.";

        if (!lobbyService.TryGetByUri(lobbyUri, out var registration))
            return $"Lobby [{lobbyUri}] not found.";

        // The host is already in the roster; everyone else registers as a player.
        // The registration mutation fires StateChanged → existing members re-project.
        IDisposable? unregisterToken = null;
        if (caller.Id != registration.State.Host.Id)
        {
            var joinResult = registration.State.RegisterPlayer(caller);
            if (joinResult.TryGetSuccess(out var token))
                unregisterToken = token;
            // A failure here (lobby closed / already registered) is non-fatal:
            // the connection still receives projections as a spectator.
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
    /// host and returns its URI.
    /// </summary>
    public async Task<string?> CreateRoom(string routeIdentifier)
    {
        if (!TryResolveCaller(out var caller, out var sessionToken))
            return null;

        var result = await lobbyService.CreateLobbyAsync(caller, routeIdentifier, Context.ConnectionAborted);
        if (!result.TryGetSuccess(out var registration))
            return null;

        // Tie lobby closure to the host's session lifecycle. When the host's last
        // connection drops and the eviction grace lapses, GameSessionState.Dispose
        // runs this dispose-action → CloseLobbyAsync, which disposes the state and
        // notifies/kicks the remaining players. A reconnect within grace cancels it.
        // Mirrors the Blazor Server Home.CreateLobby dispose-action, for the hub.
        var sessionResult = sessionServiceProvider.GetService<GameSessionState>(sessionToken);
        if (sessionResult.TryGetSuccess(out var sessionReg))
        {
            var closeAction = new DisposableAction(
                () => _ = lobbyService.CloseLobbyAsync(caller, registration, CancellationToken.None));

            if (!sessionReg.Service.TrySetCurrentSession(new UserRegistration(caller, closeAction, registration)))
            {
                logger.LogWarning(
                    "Host [{UserId}] already had an active session creating lobby [{Uri}]; " +
                    "close-on-leave not wired for it.", caller.Id, registration.Uri);
            }

            // Release the extra reference this GetService call took; the caller's
            // connection still holds its own (acquired in OnConnectedAsync), so the
            // session stays alive until the host actually leaves.
            sessionReg.LifecycleToken.Dispose();
        }

        return registration.Uri;
    }

    /// <summary>
    /// Dispatches a typed-over-the-wire command to the game engine. The resulting
    /// state mutation triggers the per-lobby fan-out automatically. Returns
    /// <c>null</c> on success or an error message.
    /// </summary>
    public async Task<string?> SubmitCommand(string lobbyUri, string command, string? payloadJson)
    {
        if (!TryResolveCaller(out var caller, out _))
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

    /// <summary>
    /// Returns the short, shareable lobby code for a room the caller is in (the
    /// human code typed on the home page's join box). The URI's random GUIDs are the
    /// access token, so any authenticated caller holding the URI may read it. Returns
    /// <c>null</c> if identity can't be resolved or the lobby is unknown.
    /// </summary>
    public string? GetLobbyCode(string lobbyUri)
    {
        if (!TryResolveCaller(out _, out _))
            return null;
        return lobbyService.TryGetByUri(lobbyUri, out var registration) ? registration.Code : null;
    }

    /// <summary>
    /// Explicit leave. When the <b>host</b> leaves, the lobby is closed immediately
    /// (state disposed, remaining players notified to leave) rather than waiting for
    /// the session-eviction grace timer. A non-host leaving is handled by the normal
    /// disconnect cleanup once their client navigates away.
    /// </summary>
    public async Task LeaveRoom(string lobbyUri)
    {
        if (!TryResolveCaller(out var caller, out _))
            return;
        if (!lobbyService.TryGetByUri(lobbyUri, out var registration))
            return;

        if (caller.Id == registration.State.Host.Id)
            await lobbyService.CloseLobbyAsync(caller, registration, CancellationToken.None);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Release the session reference; disposing the lifecycle token starts the
        // provider's 1-minute eviction grace (a reconnect within it re-acquires).
        if (registry.RemoveSession(Context.ConnectionId, out var lifecycleToken))
            lifecycleToken?.Dispose();

        // Remove the connection from its lobby and unregister the player (fires
        // PlayerUnregistered + StateChanged → remaining members re-project). The
        // per-lobby view subscription is NOT removed here — it is torn down at
        // lobby close so an empty-but-open lobby keeps projecting.
        if (registry.TryRemove(Context.ConnectionId, out _, out _, out var unregisterToken))
            unregisterToken?.Dispose();

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Resolves the caller from the handshake's signed identity token. The token
    /// arrives via SignalR's access-token mechanism (the <c>access_token</c> query
    /// parameter on the negotiate/connect request); the display name is a
    /// non-authoritative label.
    /// </summary>
    private bool TryResolveCaller(out User caller, out SessionToken sessionToken)
    {
        caller = null!;
        sessionToken = default;

        var http = Context.GetHttpContext();
        if (http is null)
            return false;

        var token = http.Request.Query["access_token"].ToString();
        if (!identityTokens.TryResolve(token, out var userId))
            return false;

        var name = http.Request.Query["userName"].ToString();
        caller = UserFactory.Create(string.IsNullOrWhiteSpace(name) ? "Player" : name, userId);
        sessionToken = new SessionToken(userId);
        return true;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
