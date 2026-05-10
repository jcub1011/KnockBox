using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Platform.Games;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Resolves a request under <c>/api/plugins/{routeIdentifier}/{**subPath}</c>
/// to a plugin engine implementing <see cref="IGameEngineHttpHandler"/> and
/// the matching room, then delegates to the handler. The dispatcher does not
/// authenticate the caller; the obfuscated room URI's two random GUIDs are
/// treated as the access token (matching the existing room-URL convention).
/// </summary>
internal sealed class PluginHttpDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ILobbyService _lobbyService;
    private readonly ILogger<PluginHttpDispatcher> _logger;

    public PluginHttpDispatcher(
        IServiceProvider services,
        ILobbyService lobbyService,
        ILogger<PluginHttpDispatcher> logger)
    {
        _services = services;
        _lobbyService = lobbyService;
        _logger = logger;
    }

    public async ValueTask<IResult> DispatchAsync(
        string routeIdentifier,
        string subPath,
        HttpContext context,
        CancellationToken ct)
    {
        var engine = _services.GetKeyedService<AbstractGameEngine>(routeIdentifier);
        if (engine is null)
            return Results.NotFound(new { error = "Unknown plugin route." });

        if (engine is not IGameEngineHttpHandler handler)
            return Results.NotFound(new { error = "Plugin does not expose HTTP endpoints." });

        if (string.IsNullOrEmpty(subPath))
            return Results.NotFound(new { error = "Missing room identifier." });

        int slash = subPath.IndexOf('/');
        string obfuscatedRoomCode = slash < 0 ? subPath : subPath[..slash];
        string handlerSubPath = slash < 0 ? string.Empty : subPath[(slash + 1)..];

        // The full lobby URI is the platform's stable identity for a room.
        // LobbyService stores it under that exact shape (`room/{route}/{guidA}-{guidB}`),
        // so reconstruct here rather than trusting GUID-pair uniqueness across routes.
        string fullUri = $"room/{routeIdentifier}/{obfuscatedRoomCode}";
        if (!_lobbyService.TryGetByUri(fullUri, out var registration))
            return Results.NotFound(new { error = "Unknown room." });

        try
        {
            return await handler.HandleAsync(context, obfuscatedRoomCode, registration.State, handlerSubPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin handler [{Route}] threw.", routeIdentifier);
            return Results.Problem(detail: "Plugin handler error.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
