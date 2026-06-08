namespace KnockBox.Core.Client.Hub;

/// <summary>
/// The strongly-typed callback surface the server's <c>GameHub</c> invokes on a
/// connected client. The browser registers handlers for these via
/// <c>HubConnection.On(...)</c>; the server pushes through
/// <c>IHubContext&lt;GameHub, IGameClient&gt;</c>.
/// </summary>
public interface IGameClient
{
    /// <summary>
    /// Delivers a per-player projected view of a room's state. <paramref name="payloadJson"/>
    /// is the recipient-specific projection (default-deny — it never carries another
    /// player's secrets). <paramref name="version"/> is monotonic per room so the client
    /// can drop out-of-order / duplicate projections after a reconnect.
    /// </summary>
    Task ReceiveView(string routeIdentifier, long version, string payloadJson);

    /// <summary>
    /// Delivers a one-shot, non-state event (toast, animation trigger, etc.).
    /// </summary>
    Task ReceiveEvent(string routeIdentifier, string eventName, string payloadJson);
}
