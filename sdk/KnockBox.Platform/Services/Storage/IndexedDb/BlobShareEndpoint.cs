using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

public static class BlobShareEndpoint
{
    /// <summary>
    /// Maps <c>GET /blob-share/{token:guid}</c>. The endpoint streams the
    /// originating circuit's blob bytes straight into the HTTP response in
    /// chunks; the server never holds more than one chunk buffer in memory
    /// for the request and never persists the bytes to disk.
    /// </summary>
    public static IEndpointConventionBuilder MapBlobShareEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/blob-share/{token:guid}", HandleAsync);
    }

    internal static async Task HandleAsync(HttpContext context, Guid token)
    {
        var services = context.RequestServices;
        var registry = services.GetRequiredService<BlobShareRegistry>();
        var logger = services.GetRequiredService<ILogger<BlobShareRegistry>>();

        var entry = registry.TryGetAndTouch(token);
        if (entry is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = entry.ContentType;
        context.Response.ContentLength = entry.Length;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = entry.CacheControl ?? "no-store, private";

        var ct = context.RequestAborted;
        // Wrap the response body so the catch handlers can tell whether any
        // bytes have already been flushed. `HttpResponse.HasStarted` works
        // in production (Kestrel flips it once headers go out) but stays
        // false under DefaultHttpContext in unit tests, so we track a
        // counter for both cases.
        var body = new CountingStream(context.Response.Body);

        // One SignalR-backed binary stream against the host's circuit; Blazor
        // frames the bytes natively (no base64, no JSON envelope) and the
        // server endpoint copies them straight into the response. The
        // CopyToAsync loop reads ChunkSize bytes at a time, so we hold at
        // most one chunk buffer in flight per request.
        try
        {
            await using var stream = await entry.StreamOpener(ct).ConfigureAwait(false);
            await stream.CopyToAsync(body, IndexedDbBlobChunking.ChunkSize, ct).ConfigureAwait(false);
            await body.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — do not abort the originating circuit.
            return;
        }
        catch (JSDisconnectedException ex)
        {
            logger.LogWarning(
                "Blob share {Token}: originating Blazor circuit disconnected mid-stream ({Message}); evicting.",
                token, ex.Message);
            registry.Remove(token);
            if (!context.Response.HasStarted && body.BytesWritten == 0)
            {
                context.Response.StatusCode = StatusCodes.Status410Gone;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Blob share {Token}: streaming failed.", token);
            if (!context.Response.HasStarted && body.BytesWritten == 0)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }

    // Pass-through Stream that tallies bytes written so the catch handlers
    // can distinguish "stream failed before any data went out" (safe to set
    // an error status) from "stream failed mid-flow" (status line already on
    // the wire, leave it alone).
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        public long BytesWritten { get; private set; }

        public CountingStream(Stream inner) { _inner = inner; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await _inner.WriteAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await _inner.WriteAsync(buffer, ct).ConfigureAwait(false);
            BytesWritten += buffer.Length;
        }
    }
}
