using System.Collections.Concurrent;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Internal entry held by <see cref="BlobShareRegistry"/>. The
/// <see cref="Fetcher"/> closure captures the originating
/// <c>IndexedDbBlobImpl</c> and resolves chunk reads through its existing
/// JS-interop pipeline.
/// </summary>
internal sealed class BlobShareEntry
{
    public required Guid Token { get; init; }
    public required string ContentType { get; init; }
    public required long Length { get; init; }
    public required Func<long, int, CancellationToken, ValueTask<byte[]>> Fetcher { get; init; }
    public string? CacheControl { get; init; }
    public DateTimeOffset? AbsoluteExpiresAt { get; init; }
    public TimeSpan? SlidingExpiry { get; init; }
    public DateTimeOffset LastAccessedUtc { get; set; } = DateTimeOffset.UtcNow;
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
    private bool _disposed;

    public BlobShareRegistry(ILogger<BlobShareRegistry> logger)
    {
        _logger = logger;
        _sweepTimer = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    internal void Register(BlobShareEntry entry)
    {
        _entries[entry.Token] = entry;
    }

    internal BlobShareEntry? TryGetAndTouch(Guid token)
    {
        if (!_entries.TryGetValue(token, out var entry)) return null;
        var now = DateTimeOffset.UtcNow;
        if (entry.AbsoluteExpiresAt is { } abs && now >= abs)
        {
            _entries.TryRemove(token, out _);
            return null;
        }
        if (entry.SlidingExpiry is { } sliding && now - entry.LastAccessedUtc > sliding)
        {
            _entries.TryRemove(token, out _);
            return null;
        }
        entry.LastAccessedUtc = now;
        return entry;
    }

    internal void Remove(Guid token)
    {
        _entries.TryRemove(token, out _);
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
            if (expired && _entries.TryRemove(kv.Key, out _)) removed++;
        }
        if (removed > 0)
        {
            _logger.LogDebug("BlobShareRegistry swept {Count} expired share(s).", removed);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
        _entries.Clear();
    }
}
