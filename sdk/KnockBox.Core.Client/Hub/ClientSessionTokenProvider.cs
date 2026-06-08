using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace KnockBox.Core.Client.Hub;

/// <summary>
/// Default <see cref="IClientSessionTokenProvider"/>. Caches the token in memory
/// for the component lifetime and persists it in the browser's
/// <c>sessionStorage</c> so a reload of the same tab re-presents it. A fresh tab
/// has empty <c>sessionStorage</c>, so it mints a new token and becomes a
/// distinct player.
/// </summary>
public sealed class ClientSessionTokenProvider(HttpClient http, IJSRuntime js)
    : IClientSessionTokenProvider
{
    private const string StorageKey = "KnockBox.SessionToken";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;

    public async ValueTask<string> GetOrIssueAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
            return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null)
                return _cached;

            var stored = await js.InvokeAsync<string?>("sessionStorage.getItem", ct, StorageKey);
            if (!string.IsNullOrWhiteSpace(stored))
                return _cached = stored;

            var issued = await http.PostAsync("api/session/token", content: null, ct);
            issued.EnsureSuccessStatusCode();
            var body = await issued.Content.ReadFromJsonAsync<TokenResponse>(ct);
            var token = body?.Token
                ?? throw new InvalidOperationException("Session token endpoint returned no token.");

            await js.InvokeVoidAsync("sessionStorage.setItem", ct, StorageKey, token);
            return _cached = token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record TokenResponse(string Token);
}
