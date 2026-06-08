namespace KnockBox.Core.Client.Hub;

/// <summary>
/// Supplies the per-tab identity token the client presents on its hub handshake.
/// The token is stored in <c>sessionStorage</c> (per browser tab, so a host-screen
/// tab and a player tab are distinct players) and minted by the server on first
/// use. A reload reuses the same token, letting the hub re-attach the same session
/// within the reconnect grace window.
/// </summary>
public interface IClientSessionTokenProvider
{
    /// <summary>
    /// Returns this tab's identity token, minting and persisting one via the
    /// server's <c>POST /api/session/token</c> endpoint on first call.
    /// </summary>
    ValueTask<string> GetOrIssueAsync(CancellationToken ct = default);
}
