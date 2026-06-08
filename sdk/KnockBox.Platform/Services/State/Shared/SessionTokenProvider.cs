using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Shared;
using Microsoft.JSInterop;

namespace KnockBox.Services.State.Shared;

/// <summary>
/// Resolves the Blazor-circuit user's <see cref="SessionToken"/> from the SAME
/// server-signed, per-tab identity token the WASM client presents to the hub.
/// <para>
/// The token lives in the browser's <c>sessionStorage</c> under
/// <see cref="StorageKey"/> (per-tab, so each tab is an independent player). It is
/// an opaque, Data-Protection-signed string; this provider unprotects it via
/// <see cref="ISessionIdentityTokenService"/> to recover the embedded user id and
/// surfaces it as a GUID-string <see cref="SessionToken"/> — the same shape the
/// old client-generated-GUID provider returned, so <c>UserService</c> and every
/// downstream consumer are unchanged. Because the circuit and the hub now resolve
/// the same token to the same id, a tab's Server pages and its hub connection
/// share one <see cref="SessionToken"/> (and therefore one cached session).
/// </para>
/// </summary>
public sealed class SessionTokenProvider(
    IJSRuntime jsRuntime,
    ISessionIdentityTokenService identityTokens,
    ILogger<SessionTokenProvider> logger) : ISessionTokenProvider
{
    /// <summary>
    /// The <c>sessionStorage</c> key. MUST match
    /// <c>KnockBox.Core.Client.Hub.ClientSessionTokenProvider.StorageKey</c> so a
    /// tab's Server circuit and WASM hub connection read the same token.
    /// </summary>
    private const string StorageKey = "KnockBox.SessionToken";

    // Technically does not need to be disposed if "AvailableWaitHandle" is never accessed.
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private SessionToken _cachedToken;

    public async ValueTask<ValueResult<SessionToken>> GetSessionTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                if (_cachedToken.HashCode == 0)
                {
                    var stored = await jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", ct, StorageKey);

                    if (!string.IsNullOrWhiteSpace(stored) && identityTokens.TryResolve(stored, out var existingId))
                    {
                        _cachedToken = new SessionToken(existingId);
                    }
                    else
                    {
                        // No token yet (fresh tab) or the stored token is tampered /
                        // foreign — mint a fresh signed token in-circuit and write it
                        // back so a later hub connection on this tab adopts the same id.
                        _cachedToken = await MintAndStoreAsync(ct);
                    }
                }

                return _cachedToken;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting session token.");
            return new ResultError("Error retrieving session token.");
        }
    }

    public async ValueTask<ValueResult<SessionToken>> ProvisionNewTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                _cachedToken = await MintAndStoreAsync(ct);
                return _cachedToken;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error provisioning session token.");
            return new ResultError("Error provisioning session token.");
        }
    }

    /// <summary>
    /// Mints a new signed identity token, persists it to <c>sessionStorage</c>, and
    /// returns the recovered id as a <see cref="SessionToken"/>. Resetting the slot
    /// also resets the tab's hub identity (a subsequent hub connection re-reads it).
    /// </summary>
    private async ValueTask<SessionToken> MintAndStoreAsync(CancellationToken ct)
    {
        var token = identityTokens.Issue();
        // Issue() always produces a resolvable token; the out-id is the embedded GUID.
        identityTokens.TryResolve(token, out var id);
        await jsRuntime.InvokeVoidAsync("sessionStorage.setItem", ct, StorageKey, token);
        return new SessionToken(id);
    }
}
