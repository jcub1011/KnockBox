using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Http;

namespace KnockBox.Platform.Hubs;

/// <summary>
/// Resolves a KnockBox caller from the signed per-tab identity token on an HTTP
/// request. Shared by <see cref="GameHub"/> (token on the SignalR handshake's
/// <c>access_token</c> query parameter) and the <c>POST /api/games/upload</c>
/// endpoint (token on the <c>Authorization: Bearer</c> header, with the same
/// query-parameter fallback), so the two transports authenticate identically
/// against <see cref="ISessionIdentityTokenService"/>.
/// </summary>
internal static class HubCallerResolver
{
    /// <summary>
    /// Resolves <paramref name="caller"/> from the request's identity token, or
    /// returns <see langword="false"/> for a missing/tampered/foreign token. The
    /// display name is read from the non-authoritative <c>userName</c> query
    /// parameter (defaulting to "Player").
    /// </summary>
    public static bool TryResolveUser(HttpContext? http, ISessionIdentityTokenService tokens, out User caller)
    {
        caller = null!;
        if (http is null)
            return false;

        if (!tokens.TryResolve(ReadToken(http), out var userId))
            return false;

        var name = http.Request.Query["userName"].ToString();
        caller = UserFactory.Create(string.IsNullOrWhiteSpace(name) ? "Player" : name, userId);
        return true;
    }

    /// <summary>
    /// Reads the identity token from the <c>Authorization: Bearer</c> header
    /// (HTTP convention) or, failing that, the <c>access_token</c> query
    /// parameter (SignalR's access-token mechanism).
    /// </summary>
    private static string ReadToken(HttpContext http)
    {
        var auth = http.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            return auth[bearer.Length..];

        return http.Request.Query["access_token"].ToString();
    }
}
