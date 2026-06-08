using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Platform.Hubs;

/// <summary>
/// Tracks which SignalR connections belong to which lobby, and the self-asserted
/// player identity behind each connection. Because per-player projections are
/// pushed per-connection (secrets must not be broadcast), the fan-out needs to
/// map <c>lobbyUri → {(connectionId, userId)}</c>; the reverse map
/// <c>connectionId → lobby</c> supports cleanup on disconnect.
/// <para>
/// Tracking the SET of connections per lobby (rather than one-per-circuit as the
/// Blazor Server model did) is what enables multi-tab: eviction/teardown is keyed
/// on the last connection leaving, not the first.
/// </para>
/// </summary>
public sealed class GameConnectionRegistry
{
    public readonly record struct ConnectionInfo(
        string LobbyUri, Guid UserId, string UserName, IDisposable? UnregisterToken);

    private readonly ConcurrentDictionary<string, ConnectionInfo> _byConnection = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Guid>> _byLobby =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records a connection. <paramref name="unregisterToken"/>, when supplied, is
    /// the player's lobby unregistration token (from <c>RegisterPlayer</c>) and is
    /// returned by <see cref="TryRemove"/> so the caller can dispose it on disconnect.
    /// </summary>
    public void Add(string connectionId, string lobbyUri, Guid userId, string userName, IDisposable? unregisterToken)
    {
        _byConnection[connectionId] = new ConnectionInfo(lobbyUri, userId, userName, unregisterToken);
        var set = _byLobby.GetOrAdd(lobbyUri, _ => new ConcurrentDictionary<string, Guid>());
        set[connectionId] = userId;
    }

    /// <summary>
    /// Removes a connection. Returns the lobby it was in (so the caller can
    /// re-project / tear down) and whether that lobby now has zero connections.
    /// </summary>
    public bool TryRemove(
        string connectionId,
        [NotNullWhen(true)] out string? lobbyUri,
        out bool lobbyNowEmpty,
        out IDisposable? unregisterToken)
    {
        lobbyNowEmpty = false;
        unregisterToken = null;
        if (!_byConnection.TryRemove(connectionId, out var info))
        {
            lobbyUri = null;
            return false;
        }

        lobbyUri = info.LobbyUri;
        unregisterToken = info.UnregisterToken;
        if (_byLobby.TryGetValue(info.LobbyUri, out var set))
        {
            set.TryRemove(connectionId, out _);
            lobbyNowEmpty = set.IsEmpty;
            if (lobbyNowEmpty)
                _byLobby.TryRemove(info.LobbyUri, out _);
        }
        return true;
    }

    public bool TryGet(string connectionId, out ConnectionInfo info)
        => _byConnection.TryGetValue(connectionId, out info);

    /// <summary>Snapshot of (connectionId, userId) for every connection in a lobby.</summary>
    public IReadOnlyList<(string ConnectionId, Guid UserId)> GetConnections(string lobbyUri)
    {
        if (!_byLobby.TryGetValue(lobbyUri, out var set))
            return [];
        return set.Select(kvp => (kvp.Key, kvp.Value)).ToArray();
    }

    public int CountForLobby(string lobbyUri)
        => _byLobby.TryGetValue(lobbyUri, out var set) ? set.Count : 0;
}
