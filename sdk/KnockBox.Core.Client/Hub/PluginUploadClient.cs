using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace KnockBox.Core.Client.Hub;

/// <summary>
/// Streams a file from a WASM game UI to the host's generic plugin upload
/// endpoint (<c>POST /api/games/upload</c>), carrying the per-tab session token
/// as a bearer so the server resolves the same identity the hub does. Any game
/// client can <c>@inject PluginUploadClient</c>; the engine receives the bytes
/// through <c>IGameUploadHandler</c>. This is the client half of the "don't
/// re-plumb uploads per plugin" contract.
/// </summary>
public sealed class PluginUploadClient(HttpClient http, IClientSessionTokenProvider tokens)
{
    /// <summary>
    /// Uploads <paramref name="content"/> to the lobby's engine. Returns
    /// <see langword="null"/> on success, or a human-readable error message the
    /// server (or transport) reported. The stream is sent as the raw request
    /// body, so it streams to the host without buffering the whole file.
    /// </summary>
    /// <param name="lobbyUri">The lobby URI the upload targets (e.g. <c>room/spardle/{code}</c>).</param>
    /// <param name="kind">The engine's upload-kind discriminator (e.g. <c>"word-pool"</c>).</param>
    /// <param name="fileName">Advisory file name passed to the handler.</param>
    /// <param name="content">The file bytes (e.g. <c>IBrowserFile.OpenReadStream(maxBytes, ct)</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string?> UploadAsync(
        string lobbyUri, string kind, string fileName, Stream content, CancellationToken ct = default)
    {
        var token = await tokens.GetOrIssueAsync(ct);

        var url = "api/games/upload"
            + $"?lobbyUri={Uri.EscapeDataString(lobbyUri)}"
            + $"&kind={Uri.EscapeDataString(kind)}"
            + $"&fileName={Uri.EscapeDataString(fileName)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return null;

        // The dispatcher returns { error = "..." } for the 400 rejections worth
        // surfacing to the host; fall back to a status-coded message otherwise.
        try
        {
            var body = await response.Content.ReadFromJsonAsync<UploadError>(ct);
            if (!string.IsNullOrWhiteSpace(body?.Error))
                return body.Error;
        }
        catch
        {
            // Non-JSON body (e.g. 401/413/499) — fall through to the generic message.
        }

        return $"Upload failed ({(int)response.StatusCode}).";
    }

    private sealed record UploadError(string? Error);
}
