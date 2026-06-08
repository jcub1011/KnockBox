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
    private long _lastVersion = -1;
    private ILogger? _logger;

    [Inject] protected GameHubConnectionFactory ConnectionFactory { get; set; } = default!;
    [Inject] protected IClientSessionTokenProvider TokenProvider { get; set; } = default!;
    [Inject] protected ILoggerFactory LoggerFactory { get; set; } = default!;

    /// <summary>Route segment of the game (matches the server plugin's RouteIdentifier).</summary>
    protected abstract string RouteIdentifier { get; }

    /// <summary>The lobby URI to join, e.g. <c>room/{route}/{code}</c>.</summary>
    protected abstract string LobbyUri { get; }

    /// <summary>Non-authoritative display name presented to the hub. Override to customise.</summary>
    protected virtual string DisplayName => "Player";

    /// <summary>The latest projected view, or <see langword="default"/> before the first projection.</summary>
    protected TView? View { get; private set; }

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
            await OnJoinFailedAsync(error);
    }

    /// <summary>Submits a typed command to the server engine for this lobby.</summary>
    protected Task<string?> SubmitCommandAsync(
        string command, string? payloadJson = null)
        => Hub.InvokeAsync<string?>("SubmitCommand", LobbyUri, command, payloadJson, ComponentDetached);

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

    /// <summary>Hook fired after a new projection is applied to <see cref="View"/>.</summary>
    protected virtual Task OnViewReceivedAsync(TView? view) => Task.CompletedTask;

    /// <summary>Hook fired when <see cref="JoinAsync"/> is rejected by the server.</summary>
    protected virtual Task OnJoinFailedAsync(string error) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _receiveViewRegistration?.Dispose();
        if (_hub is not null)
            await _hub.DisposeAsync();

        Dispose();
        GC.SuppressFinalize(this);
    }
}
