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

    private static async Task HandleAsync(HttpContext context, Guid token)
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
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers.CacheControl = entry.CacheControl ?? "no-store, private";

        var ct = context.RequestAborted;
        const int chunkSize = 16 * 1024;
        long offset = 0;
        var body = context.Response.Body;

        while (offset < entry.Length)
        {
            ct.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(chunkSize, entry.Length - offset);
            byte[] chunk;
            try
            {
                chunk = await entry.Fetcher(offset, requested, ct).ConfigureAwait(false);
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
                if (offset == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status410Gone;
                }
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Blob share {Token}: streaming failed at offset {Offset}.", token, offset);
                if (offset == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
                return;
            }

            if (chunk.Length == 0)
            {
                logger.LogWarning(
                    "Blob share {Token}: fetcher returned 0 bytes at offset {Offset} of {Length}; aborting.",
                    token, offset, entry.Length);
                return;
            }

            await body.WriteAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
            offset += chunk.Length;
        }

        await body.FlushAsync(ct).ConfigureAwait(false);
    }
}
