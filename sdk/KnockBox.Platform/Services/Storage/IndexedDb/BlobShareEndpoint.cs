using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

public static class BlobShareEndpoint
{
    // Capability URLs are immutable per registration: a fresh upload mints
    // a new token (new URL), so URL-keyed browser caching is safe. `public`
    // is correct because the token IS the capability — anyone with the URL
    // is meant to fetch. `immutable` skips revalidation entirely on cache
    // hits in supporting browsers; `max-age` caps the lifetime defensively.
    internal const string DefaultCacheControl = "public, max-age=86400, immutable";

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
        var byteCache = services.GetService<BlobShareByteCache>();
        var logger = services.GetRequiredService<ILogger<BlobShareRegistry>>();

        var entry = registry.TryGetAndTouch(token);
        if (entry is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // ETag is the token in its canonical hyphenated form, quoted per
        // RFC 7232. Capability URLs are write-once, so the ETag is constant
        // for the life of the token; If-None-Match short-circuits to 304
        // and the browser serves from its own disk cache.
        var etag = "\"" + token.ToString("D") + "\"";
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = entry.CacheControl ?? DefaultCacheControl;
            return;
        }

        context.Response.ContentType = entry.ContentType;
        context.Response.ContentLength = entry.Length;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = entry.CacheControl ?? DefaultCacheControl;
        context.Response.Headers.ETag = etag;

        var ct = context.RequestAborted;

        // Fast path: cache hit. Skip SignalR entirely; write the cached
        // bytes straight into the response. This is the common case for
        // every fetch past the first one per token (other players,
        // refreshes, second tabs).
        if (byteCache is not null && byteCache.TryGetBytes(token, out var cached))
        {
            try
            {
                await context.Response.Body.WriteAsync(cached, ct).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* client gone */ }
            return;
        }

        // Wrap the response body so the catch handlers can tell whether any
        // bytes have already been flushed. `HttpResponse.HasStarted` works
        // in production (Kestrel flips it once headers go out) but stays
        // false under DefaultHttpContext in unit tests, so we track a
        // counter for both cases.
        var body = new CountingStream(context.Response.Body);

        // Slow path: cache miss. Open the SignalR-backed binary stream
        // against the host's circuit and copy through. If the payload
        // fits the cache budget, tee the bytes into a MemoryStream as
        // we go so the next fetcher of this token serves from RAM.
        // Native binary framing (no base64, no JSON envelope) means we
        // hold one chunk buffer per request beyond the tee buffer.
        MemoryStream? teeBuffer = null;
        var canCache = byteCache is not null
            && entry.Length > 0
            && entry.Length <= byteCache.SizeLimitBytes;
        if (canCache)
        {
            teeBuffer = new MemoryStream(capacity: (int)Math.Min(entry.Length, int.MaxValue));
        }

        try
        {
            await using var stream = await entry.StreamOpener(ct).ConfigureAwait(false);
            Stream sink = teeBuffer is null ? body : new TeeStream(body, teeBuffer);
            await stream.CopyToAsync(sink, IndexedDbBlobChunking.ChunkSize, ct).ConfigureAwait(false);
            await body.FlushAsync(ct).ConfigureAwait(false);

            // Only cache after the full copy succeeds. A partial buffer
            // would serve a truncated response to the next fetcher.
            if (teeBuffer is not null && byteCache is not null)
            {
                byteCache.Store(token, teeBuffer.ToArray());
            }
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

    // Writes every chunk to two sinks (the HTTP response and an in-memory
    // tee buffer destined for the byte cache). The primary write is awaited
    // first so any response-side failure surfaces immediately; the tee
    // write goes to a MemoryStream and never throws under normal use.
    private sealed class TeeStream : Stream
    {
        private readonly Stream _primary;
        private readonly Stream _tee;

        public TeeStream(Stream primary, Stream tee)
        {
            _primary = primary;
            _tee = tee;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _primary.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _primary.Flush();
        public override Task FlushAsync(CancellationToken ct) => _primary.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _primary.Write(buffer, offset, count);
            _tee.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await _primary.WriteAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
            await _tee.WriteAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await _primary.WriteAsync(buffer, ct).ConfigureAwait(false);
            await _tee.WriteAsync(buffer, ct).ConfigureAwait(false);
        }
    }
}
