using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Platform.Games;
using KnockBox.Platform.Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Backs the platform's generic file-upload endpoint
/// (<c>POST /api/games/upload</c>). Unlike the unauthenticated, raw
/// <see cref="PluginHttpDispatcher"/>, this dispatcher centralizes the plumbing
/// an upload needs — caller authentication (signed session token), room
/// resolution, and a configurable size cap — then streams the request body to an
/// engine implementing <see cref="IGameUploadHandler"/>. A plugin author only
/// implements <c>HandleUploadAsync</c>; they never re-write this plumbing.
/// </summary>
internal sealed class PluginUploadDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ILobbyService _lobbyService;
    private readonly ISessionIdentityTokenService _identityTokens;
    private readonly KnockBoxPlatformOptions _options;
    private readonly ILogger<PluginUploadDispatcher> _logger;

    public PluginUploadDispatcher(
        IServiceProvider services,
        ILobbyService lobbyService,
        ISessionIdentityTokenService identityTokens,
        KnockBoxPlatformOptions options,
        ILogger<PluginUploadDispatcher> logger)
    {
        _services = services;
        _lobbyService = lobbyService;
        _identityTokens = identityTokens;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<IResult> DispatchAsync(
        HttpContext context,
        string lobbyUri,
        string kind,
        string fileName,
        CancellationToken ct)
    {
        if (!HubCallerResolver.TryResolveUser(context, _identityTokens, out var caller))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(lobbyUri))
            return Results.BadRequest(new { error = "Missing lobby identifier." });
        if (string.IsNullOrWhiteSpace(kind))
            return Results.BadRequest(new { error = "Missing upload kind." });

        if (!_lobbyService.TryGetByUri(lobbyUri, out var registration))
            return Results.NotFound(new { error = "Unknown room." });

        var engine = _services.GetKeyedService<AbstractGameEngine>(registration.RouteIdentifier);
        if (engine is null)
            return Results.NotFound(new { error = "Unknown plugin route." });
        if (engine is not IGameUploadHandler handler)
            return Results.BadRequest(new { error = "Game does not accept uploads." });

        // Cheap up-front rejection when the length is declared (the normal path —
        // the client posts a known-length stream). Chunked/unknown-length bodies
        // are bounded by ByteLimitStream below as a backstop.
        var max = _options.MaxUploadBytes;
        if (context.Request.ContentLength is long declared && declared > max)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        await using var limited = new ByteLimitStream(context.Request.Body, max);
        try
        {
            var result = await handler.HandleUploadAsync(caller, registration.State, kind, fileName, limited, ct);
            if (result.TryGetFailure(out var error))
            {
                _logger.LogInformation(
                    "Upload [{Kind}] rejected for [{UserId}] on [{Uri}]: {Error}",
                    kind, caller.Id, lobbyUri, error.InternalMessage);
                return Results.BadRequest(new { error = error.PublicMessage });
            }
            return Results.Ok();
        }
        catch (ByteLimitStream.LimitExceededException)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload handler [{Route}] threw.", registration.RouteIdentifier);
            return Results.Problem(detail: "Upload handler error.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Read-only pass-through that throws once more than <c>maxBytes</c> have been
    /// read, so a chunked/unknown-length body can't blow past the configured cap
    /// even though its length wasn't declared up front.
    /// </summary>
    private sealed class ByteLimitStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = await inner.ReadAsync(buffer, ct);
            Account(n);
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            Account(n);
            return n;
        }

        private void Account(int n)
        {
            _read += n;
            if (_read > maxBytes)
                throw new LimitExceededException();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public sealed class LimitExceededException : Exception;
    }
}
