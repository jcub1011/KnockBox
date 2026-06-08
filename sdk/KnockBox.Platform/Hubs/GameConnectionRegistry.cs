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

    // ── Session lifecycle tracking (independent of lobby membership) ──────────
    // The hub acquires a session-service reference on the FIRST connection for a
    // session token and releases it when the LAST one leaves, so a single tab's
    // transient reconnect double-connection doesn't evict the session. Keyed on
    // the token VALUE (each browser tab has its own token → its own session).
    private sealed class SessionEntry
    {
        public readonly HashSet<string> Connections = [];
        public IDisposable? LifecycleToken;
    }

    private readonly Lock _sessionGate = new();
    private readonly Dictionary<string, SessionEntry> _bySession = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _connectionToken = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Records <paramref name="connectionId"/> against its session
    /// <paramref name="tokenValue"/>. On the FIRST connection for that token,
    /// invokes <paramref name="acquireOnFirst"/> once and stashes the returned
    /// lifecycle token to dispose when the last connection leaves (see
    /// <see cref="RemoveSession"/>). Subsequent connections for the same token (a
    /// reconnect or a second hub negotiation for one tab) do not re-acquire.
    /// </summary>
    public void AddSession(string tokenValue, string connectionId, Func<IDisposable> acquireOnFirst)
    {
        lock (_sessionGate)
        {
            _connectionToken[connectionId] = tokenValue;

            if (!_bySession.TryGetValue(tokenValue, out var entry))
            {
                entry = new SessionEntry();
                _bySession[tokenValue] = entry;
            }

            var wasEmpty = entry.Connections.Count == 0;
            entry.Connections.Add(connectionId);
            if (wasEmpty)
                entry.LifecycleToken = acquireOnFirst();
        }
    }

    /// <summary>
    /// Removes <paramref name="connectionId"/> from its session. Returns
    /// <see langword="true"/> with the stashed <paramref name="lifecycleToken"/>
    /// only when this was the LAST connection for that session token (so the
    /// caller disposes it, starting the eviction grace period); otherwise
    /// <see langword="false"/> with a null token.
    /// </summary>
    public bool RemoveSession(string connectionId, out IDisposable? lifecycleToken)
    {
        lifecycleToken = null;
        lock (_sessionGate)
        {
            if (!_connectionToken.Remove(connectionId, out var tokenValue))
                return false;
            if (!_bySession.TryGetValue(tokenValue, out var entry))
                return false;

            entry.Connections.Remove(connectionId);
            if (entry.Connections.Count > 0)
                return false;

            _bySession.Remove(tokenValue);
            lifecycleToken = entry.LifecycleToken;
            return true;
        }
    }

    /// <summary>Test/diagnostic: number of live connections tracked for a session token.</summary>
    public int CountForSession(string tokenValue)
    {
        lock (_sessionGate)
            return _bySession.TryGetValue(tokenValue, out var entry) ? entry.Connections.Count : 0;
    }
}
