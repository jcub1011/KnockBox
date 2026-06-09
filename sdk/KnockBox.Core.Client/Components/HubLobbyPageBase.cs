using KnockBox.Core.Client.Hub;
using KnockBox.Core.Client.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace KnockBox.Core.Client.Components;

/// <summary>
/// Browser-side base for a runtime-loaded game page. Replaces the server's
/// <c>LobbyPageBase&lt;TGameState&gt;</c>: it opens the hub connection (carrying the
/// per-tab identity token), joins the lobby, applies each per-player
/// <c>ReceiveView</c> projection into a strongly-typed <typeparamref name="TView"/>
/// (dropping stale/duplicate versions after a reconnect), and tears the
/// subscription + connection down on dispose — the same disposal discipline as the
/// server base, but against the hub instead of <c>AbstractGameState</c>.
/// </summary>
/// <typeparam name="TView">The game's projected view DTO (a <c>*.Contracts</c> type).</typeparam>
public abstract class HubLobbyPageBase<TView> : DisposableComponent, IAsyncDisposable
{
    private readonly IProjectionDeserializer<TView> _deserializer;
    private HubConnection? _hub;
    private IDisposable? _receiveViewRegistration;
    private IDisposable? _receiveEventRegistration;
    private long _lastVersion = -1;
    private ILogger? _logger;

    [Inject] protected GameHubConnectionFactory ConnectionFactory { get; set; } = default!;
    [Inject] protected IClientSessionTokenProvider TokenProvider { get; set; } = default!;
    [Inject] protected ILoggerFactory LoggerFactory { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    /// <summary>Route segment of the game (matches the server plugin's RouteIdentifier).</summary>
    protected abstract string RouteIdentifier { get; }

    /// <summary>The lobby URI to join, e.g. <c>room/{route}/{code}</c>.</summary>
    protected abstract string LobbyUri { get; }

    /// <summary>Non-authoritative display name presented to the hub. Override to customise.</summary>
    protected virtual string DisplayName => "Player";

    /// <summary>The latest projected view, or <see langword="default"/> before the first projection.</summary>
    protected TView? View { get; private set; }

    /// <summary>
    /// The short, shareable lobby code (the human code typed on the home join box),
    /// fetched from the hub after joining. <see langword="null"/> until resolved.
    /// </summary>
    protected string? LobbyCode { get; private set; }

    /// <summary>The live hub connection. Throws if accessed before initialization.</summary>
    protected HubConnection Hub =>
        _hub ?? throw new InvalidOperationException("Hub connection is not yet established.");

    protected HubLobbyPageBase() : this(null) { }

    protected HubLobbyPageBase(IProjectionDeserializer<TView>? deserializer)
        => _deserializer = deserializer ?? new ReflectionProjectionDeserializer<TView>();

    protected override async Task OnInitializedAsync()
    {
        _logger = LoggerFactory.CreateLogger(GetType());

        var token = await TokenProvider.GetOrIssueAsync(ComponentDetached);
        _hub = ConnectionFactory.Create(token, DisplayName);
        _receiveViewRegistration = _hub.On<string, long, string>(
            nameof(IGameClient.ReceiveView), OnReceiveViewAsync);
        _receiveEventRegistration = _hub.On<string, string, string>(
            nameof(IGameClient.ReceiveEvent), OnReceiveEventAsync);

        await _hub.StartAsync(ComponentDetached);
        await OnHubConnectedAsync();
    }

    /// <summary>
    /// Runs after the hub connects. Default joins <see cref="LobbyUri"/>; override
    /// to customise (e.g. create-then-join flows).
    /// </summary>
    protected virtual Task OnHubConnectedAsync() => JoinAsync();

    /// <summary>Joins <see cref="LobbyUri"/>; on failure invokes <see cref="OnJoinFailedAsync"/>.</summary>
    protected async Task JoinAsync()
    {
        var error = await Hub.InvokeAsync<string?>("JoinRoom", LobbyUri, ComponentDetached);
        if (error is not null)
        {
            await OnJoinFailedAsync(error);
            return;
        }

        // Resolve the shareable short code so the game UI can show/copy it.
        LobbyCode = await Hub.InvokeAsync<string?>("GetLobbyCode", LobbyUri, ComponentDetached);
    }

    /// <summary>Submits a typed command to the server engine for this lobby.</summary>
    protected Task<string?> SubmitCommandAsync(
        string command, string? payloadJson = null)
        => Hub.InvokeAsync<string?>("SubmitCommand", LobbyUri, command, payloadJson, ComponentDetached);

    /// <summary>
    /// Leaves the lobby and navigates home. Tells the server first (so a leaving host
    /// closes the lobby immediately instead of waiting out the grace timer), then
    /// navigates regardless of whether that call succeeds.
    /// </summary>
    protected async Task LeaveAsync()
    {
        try
        {
            if (_hub is not null)
                await _hub.InvokeAsync("LeaveRoom", LobbyUri, ComponentDetached);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "LeaveRoom call failed; navigating home anyway.");
        }

        Navigation.NavigateTo("/", forceLoad: true);
    }

    private async Task OnReceiveViewAsync(string routeIdentifier, long version, string payloadJson)
    {
        // Ignore projections for other games sharing the connection, and drop
        // stale/duplicate versions (monotonic per lobby) after a reconnect.
        if (!string.Equals(routeIdentifier, RouteIdentifier, StringComparison.OrdinalIgnoreCase))
            return;
        if (version <= _lastVersion)
            return;

        try
        {
            View = _deserializer.Deserialize(payloadJson);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to deserialize projection for route [{Route}] version [{Version}].",
                routeIdentifier, version);
            return;
        }

        _lastVersion = version;
        await OnViewReceivedAsync(View);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnReceiveEventAsync(string routeIdentifier, string eventName, string payloadJson)
    {
        // Ignore events for other games sharing the connection.
        if (!string.Equals(routeIdentifier, RouteIdentifier, StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(eventName, GameClientEvents.LobbyClosed, StringComparison.Ordinal))
            await OnLobbyClosedAsync();
        else
            await OnEventReceivedAsync(eventName, payloadJson);
    }

    /// <summary>
    /// Fired when the server closes the lobby (e.g. the host left). Default leaves
    /// the room by navigating home; override to customise (e.g. show a message first).
    /// </summary>
    protected virtual Task OnLobbyClosedAsync()
    {
        Navigation.NavigateTo("/", forceLoad: true);
        return Task.CompletedTask;
    }

    /// <summary>Hook fired for a one-shot server event other than lobby-closed.</summary>
    protected virtual Task OnEventReceivedAsync(string eventName, string payloadJson) => Task.CompletedTask;

    /// <summary>Hook fired after a new projection is applied to <see cref="View"/>.</summary>
    protected virtual Task OnViewReceivedAsync(TView? view) => Task.CompletedTask;

    /// <summary>
    /// Hook fired when <see cref="JoinAsync"/> is rejected by the server — an unknown, stale, or
    /// already-closed lobby code (e.g. an old/typo'd room URL, or a route under a game's WASM prefix
    /// that isn't a real lobby). The default logs the reason and navigates home, so the page never
    /// sits stuck on its loading state waiting for a projection that will never arrive; override to
    /// surface a message first.
    /// </summary>
    protected virtual Task OnJoinFailedAsync(string error)
    {
        _logger?.LogInformation("Join rejected for [{Uri}]: {Error}. Navigating home.", LobbyUri, error);
        Navigation.NavigateTo("/", forceLoad: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Async teardown hook for a derived page's own resources (e.g. an imported
    /// <c>IJSObjectReference</c> module). Runs before the hub connection is disposed,
    /// while the circuit/runtime is still live. Default does nothing.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _receiveViewRegistration?.Dispose();
        _receiveEventRegistration?.Dispose();

        await DisposeAsyncCore();

        if (_hub is not null)
            await _hub.DisposeAsync();

        Dispose();
        GC.SuppressFinalize(this);
    }
}
