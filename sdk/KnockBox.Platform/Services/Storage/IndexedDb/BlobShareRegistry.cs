using System.Collections.Concurrent;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Internal entry held by <see cref="BlobShareRegistry"/>. The
/// <see cref="StreamOpener"/> closure captures the originating
/// <c>IndexedDbBlobImpl</c> and, when invoked, opens a single SignalR-
/// backed <see cref="Stream"/> over the host's blob via
/// <c>IJSStreamReference</c>. The endpoint then does one
/// <c>CopyToAsync</c> into the HTTP response — no per-chunk interop
/// round-trips, no base64.
/// </summary>
internal sealed class BlobShareEntry
{
    public required Guid Token { get; init; }
    public required string ContentType { get; init; }
    public required long Length { get; init; }
    public required Func<CancellationToken, ValueTask<Stream>> StreamOpener { get; init; }
    /// <summary>
    /// Opaque identifier for the originating Blazor circuit (sourced from
    /// <c>IndexedDbInterop.ScopeId</c>). BlobShareEndpoint uses it to gate
    /// concurrent <see cref="IJSStreamReference"/> opens per circuit so
    /// the display view's parallel SVG image fetches don't fan out N
    /// simultaneous JS data streams against one circuit and starve
    /// Blazor's pipe past its internal timeout.
    /// </summary>
    public required Guid CircuitScopeId { get; init; }
    public string? CacheControl { get; init; }
    public DateTimeOffset? AbsoluteExpiresAt { get; init; }
    public TimeSpan? SlidingExpiry { get; init; }
    public DateTimeOffset LastAccessedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-circuit-scope gate used by <see cref="BlobShareEndpoint"/> to
/// serialize concurrent stream opens against one originating circuit.
/// Refcounted by the number of <see cref="BlobShareEntry"/> instances
/// currently registered under the same <see cref="BlobShareEntry.CircuitScopeId"/>
/// — the registry disposes the semaphore when the last entry for that
/// scope is removed.
/// </summary>
internal sealed class BlobShareScopeGate
{
    public SemaphoreSlim Semaphore { get; } = new(initialCount: 1, maxCount: 1);
    public int RefCount;
}

/// <summary>
/// Singleton registry of currently-published blob shares. Tokens are minted
/// per share and indexed here so the <c>/blob-share/{token}</c> endpoint
/// can resolve them. Entries are evicted on disposal, on capability expiry
/// (absolute or sliding), and on circuit drop of the originating blob
/// (signaled by <see cref="JSDisconnectedException"/> bubbling out of
/// <see cref="BlobShareEntry.Fetcher"/>).
/// </summary>
public sealed class BlobShareRegistry : IDisposable
{
    private readonly ConcurrentDictionary<Guid, BlobShareEntry> _entries = new();
    private readonly ILogger<BlobShareRegistry> _logger;
    private readonly Timer _sweepTimer;
    // Optional RAM cache that fronts the share endpoint. Whenever an entry
    // is dropped from the registry (revocation, expiry, sweep) we drop the
    // cached payload too so a stale capability URL doesn't keep serving
    // bytes after its token is gone.
    private readonly BlobShareByteCache? _byteCache;
    // Per-circuit-scope gate (refcounted by registered entries). The
    // endpoint acquires this gate before opening an IJSStreamReference
    // so we never hold more than one concurrent JS data stream against
    // the same originating circuit. Mutating Register/Remove is rare
    // (only on share lifecycle), so we use a small lock to keep refcount
    // and dictionary slot in sync.
    private readonly ConcurrentDictionary<Guid, BlobShareScopeGate> _scopeGates = new();
    private readonly object _scopeGateSync = new();
    // Per-token single-flight: concurrent cache-miss requests for the
    // same token coalesce onto one underlying stream-and-store task.
    // The first thread inserts; followers await the same Task and serve
    // from the materialized byte buffer the factory returns.
    private readonly ConcurrentDictionary<Guid, Task<byte[]?>> _inflight = new();
    private bool _disposed;

    public BlobShareRegistry(ILogger<BlobShareRegistry> logger)
        : this(logger, byteCache: null) { }

    // Cache-aware overload — DI resolves both singletons so the byte cache
    // is non-null at runtime. The parameterless-cache constructor stays
    // for tests that don't care about the cache wiring.
    public BlobShareRegistry(ILogger<BlobShareRegistry> logger, BlobShareByteCache? byteCache)
    {
        _logger = logger;
        _byteCache = byteCache;
        _sweepTimer = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    internal void Register(BlobShareEntry entry)
    {
        _entries[entry.Token] = entry;
        lock (_scopeGateSync)
        {
            if (!_scopeGates.TryGetValue(entry.CircuitScopeId, out var gate))
            {
                gate = new BlobShareScopeGate();
                _scopeGates[entry.CircuitScopeId] = gate;
            }
            gate.RefCount++;
        }
    }

    /// <summary>
    /// Returns the per-scope semaphore for the originating circuit. The
    /// endpoint MUST hold this while opening the JS stream and copying
    /// bytes, so only one stream is in flight per circuit at a time.
    /// Returns <see langword="null"/> only if the scope is unknown (entry
    /// was already evicted) — caller should treat that as "share gone".
    /// </summary>
    internal SemaphoreSlim? TryGetScopeGate(Guid scopeId)
    {
        return _scopeGates.TryGetValue(scopeId, out var gate) ? gate.Semaphore : null;
    }

    /// <summary>
    /// Single-flight coordinator for a token's stream-and-store. The
    /// first call inserts the task into the in-flight table; concurrent
    /// callers receive the same Task. The entry is removed after the
    /// task settles so a future fetch (post-cache-eviction) can mint a
    /// fresh stream.
    /// </summary>
    internal Task<byte[]?> RunSingleFlight(Guid token, Func<Task<byte[]?>> factory)
    {
        return _inflight.GetOrAdd(token, _ => RunAndCleanup(token, factory));
    }

    private async Task<byte[]?> RunAndCleanup(Guid token, Func<Task<byte[]?>> factory)
    {
        try
        {
            return await factory().ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(token, out _);
        }
    }

    internal BlobShareEntry? TryGetAndTouch(Guid token)
    {
        if (!_entries.TryGetValue(token, out var entry)) return null;
        var now = DateTimeOffset.UtcNow;
        if (entry.AbsoluteExpiresAt is { } abs && now >= abs)
        {
            if (_entries.TryRemove(token, out _))
            {
                _byteCache?.Remove(token);
                ReleaseScopeRef(entry.CircuitScopeId);
            }
            return null;
        }
        if (entry.SlidingExpiry is { } sliding && now - entry.LastAccessedUtc > sliding)
        {
            if (_entries.TryRemove(token, out _))
            {
                _byteCache?.Remove(token);
                ReleaseScopeRef(entry.CircuitScopeId);
            }
            return null;
        }
        entry.LastAccessedUtc = now;
        return entry;
    }

    internal void Remove(Guid token)
    {
        if (!_entries.TryRemove(token, out var entry)) return;
        _byteCache?.Remove(token);
        ReleaseScopeRef(entry.CircuitScopeId);
    }

    private void ReleaseScopeRef(Guid scopeId)
    {
        lock (_scopeGateSync)
        {
            if (!_scopeGates.TryGetValue(scopeId, out var gate)) return;
            if (--gate.RefCount > 0) return;
            _scopeGates.TryRemove(scopeId, out _);
            gate.Semaphore.Dispose();
        }
    }

    private void Sweep()
    {
        if (_disposed) return;
        var now = DateTimeOffset.UtcNow;
        var removed = 0;
        foreach (var kv in _entries)
        {
            var entry = kv.Value;
            var expired =
                (entry.AbsoluteExpiresAt is { } abs && now >= abs) ||
                (entry.SlidingExpiry is { } sliding && now - entry.LastAccessedUtc > sliding);
            if (expired && _entries.TryRemove(kv.Key, out _))
            {
                _byteCache?.Remove(kv.Key);
                ReleaseScopeRef(entry.CircuitScopeId);
                removed++;
            }
        }
        if (removed > 0)
        {
            _logger.LogDebug("BlobShareRegistry swept {Count} expired share(s).", removed);
        }
    }

    // Test hook: runs the periodic sweep synchronously so unit tests can
    // exercise the expiry path without waiting for the 1-minute Timer.
    // Production code MUST NOT call this — the Timer drives it.
    internal void SweepForTesting() => Sweep();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
        _entries.Clear();
        lock (_scopeGateSync)
        {
            foreach (var gate in _scopeGates.Values)
            {
                gate.Semaphore.Dispose();
            }
            _scopeGates.Clear();
        }
    }
}
