using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

public static class BlobShareEndpoint
{
    // Capability URLs are token-keyed and the token-to-bytes mapping is
    // write-once, BUT a token can be revoked (host deletes an image, deletes
    // a map, or disconnects). With `immutable` + 24h max-age a revoked share
    // stays reachable from any browser/proxy that already filled its cache.
    // The 5-min must-revalidate window keeps the perf wins for hot fetches
    // (the second-tab / refresh case) while letting revocation take effect
    // soon enough for the host's "I just deleted that" expectation. The
    // If-None-Match round-trip is cheap — ETag is the token and the 304
    // path skips both SignalR and the byte cache lookup.
    internal const string DefaultCacheControl = "public, max-age=300, must-revalidate";

    // Watchdog window for opening + draining one IJSStreamReference. Sized
    // comfortably below Blazor's internal RemoteJSDataStream pipe timeout
    // (observed in production logs at ~60 s) so OUR linked CT cancels the
    // read first. Cancellation via our CT surfaces as
    // OperationCanceledException — the endpoint already handles it cleanly
    // and, crucially, Blazor does NOT report it to CircuitHost as a fatal
    // unhandled exception. Letting Blazor's own timeout fire first DID
    // tear the host circuit down (see the BlobShare timeout incident).
    internal static readonly TimeSpan DefaultPerStreamTimeout = TimeSpan.FromSeconds(45);

    // Test-only override; null means use Default. Production never touches
    // this — it's exposed so unit tests can assert the watchdog path
    // without waiting the full 45 s real-time.
    private static TimeSpan? _timeoutOverride;

    internal static TimeSpan PerStreamTimeout => _timeoutOverride ?? DefaultPerStreamTimeout;

    internal static void OverrideTimeoutForTesting(TimeSpan? value) => _timeoutOverride = value;

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
        // and the browser serves from its own disk cache. The token IS the
        // capability and is already visible in the URL, so logging the ETag
        // to upstream proxies leaks nothing the URL didn't already expose.
        var etag = "\"" + token.ToString("D") + "\"";
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = entry.CacheControl ?? DefaultCacheControl;
            return;
        }

        var ct = context.RequestAborted;

        // Fast path: cache hit. Skip SignalR entirely; write the cached
        // bytes straight into the response. This is the common case for
        // every fetch past the first one per token (other players,
        // refreshes, second tabs).
        if (byteCache is not null && byteCache.TryGetBytes(token, out var cached))
        {
            context.Response.ContentType = entry.ContentType;
            context.Response.ContentLength = entry.Length;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = entry.CacheControl ?? DefaultCacheControl;
            context.Response.Headers.ETag = etag;
            try
            {
                await context.Response.Body.WriteAsync(cached, ct).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* client gone */ }
            return;
        }

        // Slow path: cache miss. We have to open a SignalR-backed stream
        // against the originating circuit. Three protections layered here:
        //
        //  (1) Watchdog CTS — a linked CT that cancels at PerStreamTimeout
        //      (45 s), comfortably below Blazor's internal pipe-read timeout
        //      (~60 s). When OUR CT fires first, the in-flight ReadAsync
        //      throws OperationCanceledException; Blazor does not escalate
        //      that to CircuitHost.UnhandledException, so the host circuit
        //      stays alive. Letting Blazor's own timeout fire first DID
        //      kill the host (the original incident).
        //
        //  (2) Per-circuit-scope semaphore — only one JS data stream open
        //      at a time per originating circuit. Without it, a display
        //      view opening with N images fans out N parallel JS streams
        //      against one circuit, starving the JS dispatcher and making
        //      one of them miss the timeout window. With the byte cache
        //      fronting all subsequent fetches, the serial drain only
        //      pays the stream cost once per distinct blob.
        //
        //  (3) Per-token single-flight — concurrent requests for the same
        //      token coalesce onto one stream-and-cache task and all serve
        //      from the resulting byte buffer. No duplicate streams; no
        //      duplicate cache writes.
        //
        // On any failure we fall through to placeholder-friendly status
        // codes: 503 for transient/timeout (display view should retry on
        // next render), 410 for revoked circuit, 500 for unexpected.

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
        watchdog.CancelAfter(PerStreamTimeout);
        var watchdogCt = watchdog.Token;

        var gate = registry.TryGetScopeGate(entry.CircuitScopeId);
        if (gate is null)
        {
            // Entry was evicted between TryGetAndTouch and here — treat as
            // gone rather than racing further.
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }

        try
        {
            await gate.WaitAsync(watchdogCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // client disconnected while queued
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Blob share {Token}: timed out waiting for per-circuit gate ({Scope}).",
                token, entry.CircuitScopeId);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        catch (ObjectDisposedException)
        {
            // Gate disposed mid-wait (last entry for this scope was removed).
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }

        byte[]? payload;
        try
        {
            // Re-check cache after acquiring — a peer may have just populated
            // it while we waited at the gate.
            if (byteCache is not null && byteCache.TryGetBytes(token, out var fresh))
            {
                payload = fresh.ToArray();
            }
            else
            {
                payload = await registry.RunSingleFlight(token, async () =>
                {
                    // Inside the gate: open the JS stream, drain into RAM,
                    // store in the byte cache (if it fits), and return the
                    // bytes so the response can be written from them.
                    Stream sourceStream;
                    try
                    {
                        sourceStream = await entry.StreamOpener(watchdogCt).ConfigureAwait(false);
                    }
                    catch (JSDisconnectedException ex)
                    {
                        logger.LogWarning(
                            "Blob share {Token}: originating Blazor circuit disconnected before stream open ({Message}); evicting.",
                            token, ex.Message);
                        registry.Remove(token);
                        throw;
                    }

                    await using (sourceStream)
                    {
                        using var buffer = new MemoryStream(
                            capacity: (int)Math.Min(Math.Max(entry.Length, 0), int.MaxValue));
                        await sourceStream.CopyToAsync(
                            buffer, IndexedDbBlobChunking.ChunkSize, watchdogCt).ConfigureAwait(false);
                        var bytes = buffer.ToArray();

                        // Only cache after the full copy succeeds. A partial
                        // buffer would serve a truncated response to the next
                        // fetcher. byteCache.Store itself rejects oversize
                        // payloads (>SizeLimitBytes), so the size check is
                        // delegated.
                        if (byteCache is not null && bytes.Length > 0)
                        {
                            byteCache.Store(token, bytes);
                        }
                        return bytes;
                    }
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — do not change status, do not blame the
            // originating circuit.
            return;
        }
        catch (OperationCanceledException)
        {
            // Watchdog fired — Blazor's internal pipe timeout would have
            // followed within ~15 s and escalated to a circuit-fatal
            // exception. By cancelling first we keep the host circuit alive.
            logger.LogError(
                "Blob share {Token}: source stream watchdog elapsed after {Timeout}; returning 503.",
                token, PerStreamTimeout);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
            return;
        }
        catch (JSDisconnectedException ex)
        {
            logger.LogWarning(
                "Blob share {Token}: originating Blazor circuit disconnected mid-stream ({Message}); evicting.",
                token, ex.Message);
            registry.Remove(token);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status410Gone;
            }
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Blob share {Token}: streaming failed.", token);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
            return;
        }
        finally
        {
            try { gate.Release(); }
            catch (ObjectDisposedException) { /* registry tore down the gate; benign */ }
            catch (SemaphoreFullException) { /* defensive — should not happen */ }
        }

        if (payload is null)
        {
            // Single-flight returned null — treat as transient miss.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
            return;
        }

        // Write response from the materialized buffer. Headers are set here
        // (not before stream open) so any failure path above can land a
        // clean status code without HasStarted blocking it.
        context.Response.ContentType = entry.ContentType;
        context.Response.ContentLength = payload.Length;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = entry.CacheControl ?? DefaultCacheControl;
        context.Response.Headers.ETag = etag;
        try
        {
            await context.Response.Body.WriteAsync(payload, ct).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client gone after we already buffered the bytes. The cache
            // is already populated, so the next fetcher is fast.
        }
    }

}
