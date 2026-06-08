namespace KnockBox.Core.Services.State.Shared;

/// <summary>
/// Mints and resolves the per-tab identity token a circuit-free SignalR client
/// presents on its handshake. The token binds a server-generated user id into a
/// tamper-proof string (signed server-side); the client cannot forge a different
/// id. The client stores it in <c>sessionStorage</c> (per browser tab) so each
/// tab is an independent player and a reload re-presents the same id.
/// </summary>
/// <remarks>
/// This replaces the Phase 0 spike's self-asserted query-string identity, where
/// the client supplied a raw <c>userId</c> the hub trusted blindly.
/// </remarks>
public interface ISessionIdentityTokenService
{
    /// <summary>
    /// Mints a new identity token bound to a fresh server-generated user id.
    /// </summary>
    string Issue();

    /// <summary>
    /// Resolves a previously-issued token back to its user id. Returns
    /// <see langword="false"/> for a tampered, malformed, or foreign token.
    /// </summary>
    bool TryResolve(string? token, out Guid userId);
}
