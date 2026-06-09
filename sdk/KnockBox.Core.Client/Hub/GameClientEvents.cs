namespace KnockBox.Core.Client.Hub;

/// <summary>
/// Well-known one-shot event names pushed via <see cref="IGameClient.ReceiveEvent"/>.
/// Shared by the server (which raises them) and the client base (which reacts).
/// </summary>
public static class GameClientEvents
{
    /// <summary>The lobby was closed (e.g. the host left). Clients should leave.</summary>
    public const string LobbyClosed = "lobby-closed";
}
