using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Process-wide, bounded RAM cache for blob-share payloads. The first GET
/// for a token streams bytes through SignalR from the originating circuit
/// (the existing <see cref="BlobShareEndpoint"/> behaviour); subsequent
/// GETs for the same token serve directly out of this cache without
/// re-traversing the host's circuit.
/// <para>
/// <b>Eviction contract</b> — delivered by <see cref="MemoryCache"/>:
/// <list type="bullet">
/// <item><b>LRU under capacity pressure</b>. When the working set exceeds
/// <see cref="SizeLimitBytes"/>, <c>MemoryCache.Compact</c> walks entries
/// in <c>LastAccessed</c> ascending order within a priority bucket and
/// removes oldest first until back under the low-water mark. All entries
/// are stored at the default Normal priority, so this is pure LRU.
/// <see cref="HotEntry_SurvivesLruEviction"/> in
/// <c>BlobShareEndpointTests</c> locks this contract in.</item>
/// <item><b>30-min idle eviction</b>. Each entry carries a
/// <c>SlidingExpiration = </c><see cref="DefaultSlidingExpiration"/>;
/// every <see cref="TryGetBytes"/> hit refreshes it, so a blob nobody
/// asks about for the window drops on the next scan.</item>
/// <item><b>LastAccessed tracking</b>. <see cref="MemoryCache"/>
/// updates the per-entry timestamp on every internal <c>TryGetValue</c>
/// — which is what backs <see cref="TryGetBytes"/>. Stores also set
/// it, so a freshly-written entry starts as the newest.</item>
/// </list>
/// </para>
/// <para>
/// Holds the architecture's spirit: bytes are RAM-only, never persisted
/// to disk; entries are evicted alongside their registry tokens (see
/// <see cref="BlobShareRegistry"/> wiring) so a revoked share doesn't
/// leave a stale copy alive; entries larger than the ceiling bypass the
/// cache so a single oversized blob can't evict everything else.
/// </para>
/// <para>
/// <b>Observability</b>. <see cref="MemoryCacheOptions.TrackStatistics"/>
/// is on, so <c>MemoryCache.GetCurrentStatistics</c> tracks hits,
/// misses, entry count, and estimated bytes. A 5-minute timer emits one
/// INFO-level summary log per period including an eviction-reason
/// histogram populated by per-entry <c>PostEvictionCallback</c>. The
/// histogram is the diagnostic that answers "are we evicting because
/// of capacity (real pressure) or expiry (cold dormant entries)?" —
/// the signal we'd need before considering per-room budgets or a
/// bigger ceiling.
/// </para>
/// </summary>
public sealed class BlobShareByteCache : IDisposable
{
    /// <summary>
    /// Default cache budget in bytes. Sized to roughly 25 × 10 MB images;
    /// LRU evicts beyond this. Bigger ceilings have to be weighed against
    /// per-process working-set pressure — a server hosting several busy
    /// rooms shares this one cache. Tests override per-instance via the
    /// constructor parameter so they don't have to allocate 256 MB just
    /// to exercise the oversize-bypass path.
    /// </summary>
    public const long DefaultSizeLimitBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Default sliding-expiration window. Entries that haven't been
    /// touched for this long are evicted by <see cref="MemoryCache"/>'s
    /// background scan. Reads via <see cref="TryGetBytes"/> refresh the
    /// window — a steady stream of fetches keeps a hot blob resident
    /// indefinitely (the LRU ceiling is the only ceiling there). A
    /// blob that nobody asks about for 30 minutes drops; the next
    /// fetcher pays the SignalR cost again to repopulate.
    /// </summary>
    public static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Period between observability summary log lines. Chosen long enough
    /// that the log doesn't add noticeable line volume to logs, short
    /// enough that an operator can correlate a thrashing complaint with
    /// the next cache report (~5 min latency).
    /// </summary>
    public static readonly TimeSpan DefaultSummaryPeriod = TimeSpan.FromMinutes(5);

    private readonly MemoryCache _cache;
    private readonly ILogger<BlobShareByteCache> _logger;
    private readonly long _sizeLimit;
    private readonly TimeSpan _slidingExpiration;
    private readonly Timer? _summaryTimer;
    // Eviction reason histogram. Reset to zero each time the summary
    // timer logs and flushes. Concurrent because PostEvictionCallback
    // fires from MemoryCache's worker (any thread) and the timer reads
    // the same dictionary on its own thread.
    private readonly ConcurrentDictionary<EvictionReason, long> _evictionCounts = new();
    // Cached PostEvictionCallback delegate. MemoryCacheEntryOptions
    // accepts a list of callbacks per-entry; making it static avoids
    // allocating a new delegate per Store.
    private static readonly PostEvictionDelegate _onEvicted = OnEvicted;
    private long _lastHits;
    private long _lastMisses;
    private bool _disposed;

    /// <summary>Current per-instance ceiling (bytes). Equals <see cref="DefaultSizeLimitBytes"/> unless overridden.</summary>
    public long SizeLimitBytes => _sizeLimit;

    /// <summary>Current per-instance sliding-expiration window. Equals <see cref="DefaultSlidingExpiration"/> unless overridden.</summary>
    public TimeSpan SlidingExpiration => _slidingExpiration;

    public BlobShareByteCache(ILogger<BlobShareByteCache> logger)
        : this(logger, DefaultSizeLimitBytes, DefaultSlidingExpiration, DefaultSummaryPeriod) { }

    // The size-limit override exists for tests (so they can assert the
    // oversize-bypass path without allocating 256 MB) and for future per-
    // host tuning. Production registration uses the parameterless overload.
    public BlobShareByteCache(ILogger<BlobShareByteCache> logger, long sizeLimitBytes)
        : this(logger, sizeLimitBytes, DefaultSlidingExpiration, DefaultSummaryPeriod) { }

    public BlobShareByteCache(
        ILogger<BlobShareByteCache> logger,
        long sizeLimitBytes,
        TimeSpan slidingExpiration)
        : this(logger, sizeLimitBytes, slidingExpiration, DefaultSummaryPeriod) { }

    // Full constructor. `summaryPeriod = Zero` disables the periodic log —
    // tests use that to avoid Timer noise; production uses the default.
    public BlobShareByteCache(
        ILogger<BlobShareByteCache> logger,
        long sizeLimitBytes,
        TimeSpan slidingExpiration,
        TimeSpan summaryPeriod)
    {
        if (sizeLimitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeLimitBytes));
        if (slidingExpiration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(slidingExpiration));
        if (summaryPeriod < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(summaryPeriod));
        _logger = logger;
        _sizeLimit = sizeLimitBytes;
        _slidingExpiration = slidingExpiration;
        // SizeLimit is in the unit each entry chooses for its Size — we use
        // byte length. CompactionPercentage is the fraction LRU evicts once
        // the ceiling is hit; 0.25 trims the coldest quarter so we don't
        // thrash on a sustained miss stream. TrackStatistics is on so
        // GetCurrentStatistics() gives us hits/misses/count/bytes for the
        // 5-min summary log without rolling our own counters.
        // SizeLimit and each entry's Size MUST stay in the same unit (bytes
        // here). Mixing units — e.g., setting Size = 1 on some entries —
        // would silently turn the byte ceiling into an entry-count ceiling
        // for those entries and let the cache exceed its memory budget.
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = sizeLimitBytes,
            CompactionPercentage = 0.25,
            TrackStatistics = true,
        });
        if (summaryPeriod > TimeSpan.Zero)
        {
            _summaryTimer = new Timer(_ => LogSummary(), state: null, summaryPeriod, summaryPeriod);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> and the cached bytes if the token's
    /// payload is in cache. The returned memory is the SAME backing array
    /// the cache holds — callers must treat it as read-only.
    /// </summary>
    public bool TryGetBytes(Guid token, out ReadOnlyMemory<byte> bytes)
    {
        if (_disposed) { bytes = default; return false; }
        if (_cache.TryGetValue(token, out byte[]? arr) && arr is not null)
        {
            bytes = arr;
            return true;
        }
        bytes = default;
        return false;
    }

    /// <summary>
    /// Stores <paramref name="payload"/> under <paramref name="token"/>.
    /// Skips entries larger than the cache ceiling so a single rogue
    /// upload can't blow the budget — the caller still serves the
    /// in-flight request from its own buffer, only the caching is
    /// declined.
    /// </summary>
    public void Store(Guid token, byte[] payload)
    {
        if (_disposed) return;
        if (payload is null || payload.Length == 0) return;
        if (payload.LongLength > _sizeLimit)
        {
            _logger.LogDebug(
                "Blob share {Token} payload ({Bytes} B) exceeds cache ceiling ({Limit} B); not cached.",
                token, payload.LongLength, _sizeLimit);
            return;
        }
        var options = new MemoryCacheEntryOptions
        {
            Size = payload.LongLength,
            // Sliding window: every TryGetBytes hit refreshes it. A cold
            // entry — one nobody asks about for the window — drops on the
            // next compaction scan and the next fetcher repopulates from
            // the host's circuit. Independent of the LRU ceiling: the
            // ceiling caps total working set, the slide caps per-entry
            // dormancy.
            SlidingExpiration = _slidingExpiration,
        };
        // Per-entry eviction callback. `state = this` so the static
        // handler can attribute counts without allocating a closure per
        // Store. EvictionReason tells us why each entry left:
        //  Capacity / TokenExpired → real pressure (size or sliding)
        //  Removed                  → explicit Remove (registry revoke)
        //  Replaced                 → Store overwrote an existing token
        // The histogram in the 5-min summary makes thrashing legible.
        options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = _onEvicted,
            State = this,
        });
        _cache.Set(token, payload, options);
    }

    /// <summary>
    /// Drops the cache entry for <paramref name="token"/>. Called by
    /// <see cref="BlobShareRegistry"/> when a share is revoked
    /// (explicit removal or sweep) so a stale cached copy doesn't
    /// outlive its capability URL.
    /// </summary>
    public void Remove(Guid token)
    {
        if (_disposed) return;
        _cache.Remove(token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _summaryTimer?.Dispose();
        _cache.Dispose();
    }

    // PostEvictionCallback handler. Bumps the histogram bucket for the
    // given reason. Allocation-free under steady state — the dictionary
    // is GetOrAdd-cached and the increment is interlocked.
    private static void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (state is not BlobShareByteCache self) return;
        self._evictionCounts.AddOrUpdate(reason, 1, static (_, v) => v + 1);
    }

    // Emits the periodic summary line. Reads MemoryCache stats, snapshots
    // and clears the eviction histogram so each period reports a delta,
    // not a running total. Anything throwing here is swallowed — the
    // Timer thread must not propagate.
    private void LogSummary()
    {
        if (_disposed) return;
        try
        {
            var stats = _cache.GetCurrentStatistics();
            // GetCurrentStatistics can return null if TrackStatistics is off
            // mid-flight (defensive — we always set it on).
            if (stats is null) return;

            // Delta hits/misses since the last summary; running totals
            // would obscure recent behaviour as the process ages.
            var hits = stats.TotalHits - _lastHits;
            var misses = stats.TotalMisses - _lastMisses;
            _lastHits = stats.TotalHits;
            _lastMisses = stats.TotalMisses;

            // Snapshot + clear the histogram. AddOrUpdate writes are concurrent
            // with the reads below; if a race drops a count we don't care —
            // the next period catches up.
            var capacity = _evictionCounts.TryGetValue(EvictionReason.Capacity, out var c) ? c : 0;
            var expired = _evictionCounts.TryGetValue(EvictionReason.TokenExpired, out var t) ? t : 0;
            var removed = _evictionCounts.TryGetValue(EvictionReason.Removed, out var r) ? r : 0;
            var replaced = _evictionCounts.TryGetValue(EvictionReason.Replaced, out var rp) ? rp : 0;
            _evictionCounts.Clear();

            var total = hits + misses;
            // Hit rate is the headline number. If misses dominate while
            // bytes is pinned near ceiling and Capacity evictions are
            // climbing, that's the thrashing signature.
            var hitRate = total > 0 ? (double)hits / total : 0.0;

            _logger.LogInformation(
                "BlobShareByteCache summary: hits={Hits} misses={Misses} hitRate={HitRate:P1} " +
                "entries={Entries} bytes={Bytes}/{Ceiling} " +
                "evictions[capacity={EvCapacity} expired={EvExpired} removed={EvRemoved} replaced={EvReplaced}]",
                hits, misses, hitRate,
                stats.CurrentEntryCount, stats.CurrentEstimatedSize ?? 0, _sizeLimit,
                capacity, expired, removed, replaced);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BlobShareByteCache summary log failed.");
        }
    }

    // Test hook: runs MemoryCache.Compact synchronously so a unit test can
    // assert LRU ordering without racing the auto-compaction worker that
    // OvercapacityCompaction spins up on a background thread. Production
    // code MUST NOT call this — auto-compaction triggers itself when
    // SizeLimit is exceeded.
    internal void CompactForTesting(double percentage)
    {
        if (_disposed) return;
        _cache.Compact(percentage);
    }

    // Test hook: synchronously runs the periodic summary handler so a
    // unit test can assert log output without waiting for the Timer
    // (or constructing one at a sub-second period).
    internal void TriggerSummaryForTesting() => LogSummary();

    // Test hook: counts in the live histogram. Returns 0 for any
    // unrecorded reason. Snapshot only — the bucket may keep changing.
    internal long GetEvictionCountForTesting(EvictionReason reason)
        => _evictionCounts.TryGetValue(reason, out var v) ? v : 0;
}
