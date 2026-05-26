using System.Text;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class BlobShareEndpointTests
{
    // Builds a cache with the periodic Timer disabled so unit tests don't
    // allocate 5-minute Timers into the runtime queue per test.
    private static BlobShareByteCache CreateTestCache(
        long? sizeLimit = null,
        TimeSpan? slidingExpiration = null)
        => new(
            NullLogger<BlobShareByteCache>.Instance,
            sizeLimit ?? BlobShareByteCache.DefaultSizeLimitBytes,
            slidingExpiration ?? BlobShareByteCache.DefaultSlidingExpiration,
            summaryPeriod: TimeSpan.Zero);

    private static (HttpContext ctx, MemoryStream body, BlobShareRegistry registry, BlobShareByteCache? cache) MakeContext(
        BlobShareRegistry? sharedRegistry = null,
        BlobShareByteCache? sharedCache = null,
        bool withCache = true)
    {
        var cache = sharedCache ?? (withCache ? CreateTestCache() : null);
        var registry = sharedRegistry ?? new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
        var services = new ServiceCollection();
        services.AddSingleton(registry);
        if (cache is not null) services.AddSingleton(cache);
        services.AddSingleton<ILogger<BlobShareRegistry>>(NullLogger<BlobShareRegistry>.Instance);
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = provider };
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body, registry, cache);
    }

    // The new BlobShareEntry exposes a StreamOpener: one call yields a single
    // Stream that the endpoint copies into the response. Tests inject a
    // backing byte[] (wrapped in MemoryStream) or a throwing opener.
    // CircuitScopeId defaults to a fresh Guid so independent tests don't
    // accidentally serialize on a shared per-scope gate; the new concurrency
    // tests override this to force same-scope serialization where intended.
    private static BlobShareEntry MakeEntry(
        byte[] payload,
        string contentType = "application/octet-stream",
        Func<CancellationToken, ValueTask<Stream>>? streamOpener = null,
        string? cacheControl = null,
        Guid? circuitScopeId = null)
        => new()
        {
            Token = Guid.NewGuid(),
            ContentType = contentType,
            Length = payload.LongLength,
            CacheControl = cacheControl,
            CircuitScopeId = circuitScopeId ?? Guid.NewGuid(),
            StreamOpener = streamOpener
                ?? (_ => ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false))),
        };

    [TestMethod]
    public async Task UnknownToken_Returns_404()
    {
        var (ctx, _, _, _) = MakeContext();

        await BlobShareEndpoint.HandleAsync(ctx, Guid.NewGuid());

        Assert.AreEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task KnownToken_StreamsFullBody_AndSetsHeaders()
    {
        var (ctx, body, registry, _) = MakeContext();
        var payload = new byte[IndexedDbBlobChunking.ChunkSize * 2 + 7];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xff);
        var entry = MakeEntry(payload, "image/png");
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.AreEqual("image/png", ctx.Response.ContentType);
        Assert.AreEqual(payload.LongLength, ctx.Response.ContentLength);
        Assert.AreEqual("nosniff", ctx.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.AreEqual(BlobShareEndpoint.DefaultCacheControl, ctx.Response.Headers.CacheControl.ToString());
        // Capability URLs are immutable per registration, so the ETag is just
        // the token; browser cache hits on If-None-Match short-circuit to 304.
        Assert.AreEqual("\"" + entry.Token.ToString("D") + "\"", ctx.Response.Headers.ETag.ToString());
        CollectionAssert.AreEqual(payload, body.ToArray());
    }

    [TestMethod]
    public async Task CustomCacheControl_IsApplied()
    {
        var (ctx, _, registry, _) = MakeContext();
        var entry = MakeEntry(Encoding.UTF8.GetBytes("hi"), cacheControl: "public, max-age=60");
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual("public, max-age=60", ctx.Response.Headers.CacheControl.ToString());
    }

    [TestMethod]
    public async Task IfNoneMatch_WithMatchingEtag_Returns_304_AndSkipsStreamOpener()
    {
        var (ctx, body, registry, _) = MakeContext();
        var openerCalls = 0;
        var entry = MakeEntry(
            new byte[16],
            streamOpener: _ =>
            {
                openerCalls++;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            });
        registry.Register(entry);
        ctx.Request.Headers.IfNoneMatch = "\"" + entry.Token.ToString("D") + "\"";

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status304NotModified, ctx.Response.StatusCode);
        Assert.AreEqual(0, openerCalls, "StreamOpener must not be invoked on a 304 short-circuit.");
        Assert.AreEqual(0, body.Length);
    }

    [TestMethod]
    public async Task JsDisconnectedException_BeforeAnyBytes_Returns_410_AndEvicts()
    {
        var (ctx, _, registry, _) = MakeContext();
        var entry = MakeEntry(
            new byte[64],
            streamOpener: _ => throw new JSDisconnectedException("circuit gone"));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status410Gone, ctx.Response.StatusCode);
        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public async Task UnexpectedException_BeforeAnyBytes_Returns_500()
    {
        var (ctx, _, registry, _) = MakeContext();
        var entry = MakeEntry(
            new byte[64],
            streamOpener: _ => throw new IOException("boom"));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task MidStream_Disconnect_Returns_410_WithEmptyBody()
    {
        var (ctx, body, registry, _) = MakeContext();
        // A stream that yields one full ChunkSize block then throws on the
        // next ReadAsync. The endpoint now drains the JS stream fully into a
        // RAM buffer before writing the response, so a mid-stream failure
        // surfaces as 410 with an empty body rather than 200 with a partial
        // body. This is the better contract — clients never see truncated
        // payloads — and matches what the display view's onerror handler
        // expects for a placeholder swap.
        var entry = MakeEntry(
            new byte[IndexedDbBlobChunking.ChunkSize * 3],
            streamOpener: _ => ValueTask.FromResult<Stream>(
                new ThrowAfterFirstChunkStream(IndexedDbBlobChunking.ChunkSize)));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status410Gone, ctx.Response.StatusCode);
        Assert.AreEqual(0, body.Length);
        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public async Task SecondFetch_ServesFromCache_WithoutReopeningStream()
    {
        // First request: cache miss → StreamOpener invoked, response written,
        // cache populated as a side effect. Second request against the same
        // token: cache hit → no StreamOpener call, identical body.
        var cache = CreateTestCache();
        var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
        var payload = new byte[IndexedDbBlobChunking.ChunkSize + 13];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 7) & 0xff);
        var openerCalls = 0;
        var entry = MakeEntry(payload, streamOpener: _ =>
        {
            openerCalls++;
            return ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false));
        });
        registry.Register(entry);

        var (ctx1, body1, _, _) = MakeContext(registry, cache);
        await BlobShareEndpoint.HandleAsync(ctx1, entry.Token);
        Assert.AreEqual(1, openerCalls);
        CollectionAssert.AreEqual(payload, body1.ToArray());

        var (ctx2, body2, _, _) = MakeContext(registry, cache);
        await BlobShareEndpoint.HandleAsync(ctx2, entry.Token);
        Assert.AreEqual(1, openerCalls, "Second fetch must serve from cache without re-opening the SignalR stream.");
        CollectionAssert.AreEqual(payload, body2.ToArray());
    }

    [TestMethod]
    public async Task RegistryRemove_EvictsCachedBytes()
    {
        var cache = CreateTestCache();
        var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
        var payload = Encoding.UTF8.GetBytes("evictable");
        var entry = MakeEntry(payload);
        registry.Register(entry);

        var (ctx1, _, _, _) = MakeContext(registry, cache);
        await BlobShareEndpoint.HandleAsync(ctx1, entry.Token);
        Assert.IsTrue(cache.TryGetBytes(entry.Token, out _), "First fetch must populate the byte cache.");

        registry.Remove(entry.Token);

        Assert.IsFalse(cache.TryGetBytes(entry.Token, out _),
            "Removing the registry entry must also evict the cached bytes — a revoked capability URL must not keep serving from RAM.");
    }

    [TestMethod]
    public void OversizeEntry_BypassesCache()
    {
        // The store call itself short-circuits; this keeps a single rogue
        // upload from evicting the rest of the cache. Uses a 1 KB ceiling
        // so the test doesn't have to allocate 256 MB just to exercise
        // the bypass.
        var cache = CreateTestCache(sizeLimit: 1024);
        var token = Guid.NewGuid();
        var oversize = new byte[2048];

        cache.Store(token, oversize);

        Assert.IsFalse(cache.TryGetBytes(token, out _));
    }

    [TestMethod]
    public async Task IdleEntry_ExpiresAfterSlidingWindow()
    {
        // Uses a 50ms sliding window so the test runs in real time without
        // having to mock the clock. Production sets 30 minutes — same code
        // path. MemoryCache evicts expired entries on the next access, so
        // querying after the window's gone is enough to surface the miss.
        var cache = CreateTestCache(slidingExpiration: TimeSpan.FromMilliseconds(50));
        var token = Guid.NewGuid();
        cache.Store(token, new byte[16]);

        Assert.IsTrue(cache.TryGetBytes(token, out _), "Entry should be present immediately after store.");

        await Task.Delay(200);

        Assert.IsFalse(cache.TryGetBytes(token, out _),
            "Idle entry must expire after the sliding window so dormant blobs don't sit in RAM forever.");
    }

    [TestMethod]
    public void HotEntry_SurvivesLruEviction()
    {
        // Locks in the LRU contract: when MemoryCache evicts on capacity
        // pressure, it removes oldest-LastAccessed first within a priority
        // bucket. All our entries are at the default Normal priority, so
        // this is pure LRU. A future MemoryCache change that altered the
        // ordering would break this test — exactly what we want.
        var cache = CreateTestCache(sizeLimit: 16 * 1024);
        var cold1 = Guid.NewGuid();
        var cold2 = Guid.NewGuid();
        var cold3 = Guid.NewGuid();
        var hot = Guid.NewGuid();

        // Order matters: oldest LastAccessed goes first under LRU. UtcNow
        // resolution on Windows is ~15 ms, so a short sleep between sets
        // makes the timestamps strictly monotonic.
        cache.Store(cold1, new byte[1024]);
        Thread.Sleep(20);
        cache.Store(cold2, new byte[1024]);
        Thread.Sleep(20);
        cache.Store(cold3, new byte[1024]);
        Thread.Sleep(20);
        cache.Store(hot, new byte[1024]);
        Thread.Sleep(20);

        // Re-touch hot via the public API so its LastAccessed becomes the
        // freshest. This is the spec the user wants verified: a steady
        // stream of fetches keeps the entry resident.
        Assert.IsTrue(cache.TryGetBytes(hot, out _));

        // Aggressive compaction — Compact(percentage) drops `percentage *
        // Count` entries by LRU. 0.75 × 4 = 3 entries gone; with hot the
        // youngest, all three cold ones go.
        cache.CompactForTesting(0.75);

        Assert.IsTrue(cache.TryGetBytes(hot, out _), "Hot entry must survive LRU eviction.");
        var coldEvicted = (cache.TryGetBytes(cold1, out _) ? 0 : 1)
                       + (cache.TryGetBytes(cold2, out _) ? 0 : 1)
                       + (cache.TryGetBytes(cold3, out _) ? 0 : 1);
        Assert.IsTrue(coldEvicted >= 2,
            $"Expected at least 2 cold entries evicted by LRU; got {coldEvicted}.");
    }

    [TestMethod]
    public void EvictionHistogram_ClassifiesByReason()
    {
        // Locks in the observability contract: every eviction reaches the
        // PostEvictionCallback and is binned by EvictionReason so the
        // 5-min summary log can answer "capacity pressure or just idle?"
        // PostEvictionCallback dispatches on the ThreadPool, so reads
        // spin briefly to let the callbacks settle.
        var cache = CreateTestCache(sizeLimit: 4 * 1024);

        // Replaced: a Store under an existing token swaps the payload.
        var replacedToken = Guid.NewGuid();
        cache.Store(replacedToken, new byte[256]);
        cache.Store(replacedToken, new byte[256]);

        // Removed: explicit Remove from the public API.
        var removedToken = Guid.NewGuid();
        cache.Store(removedToken, new byte[256]);
        cache.Remove(removedToken);

        // Capacity: compaction drops oldest entries.
        for (var i = 0; i < 4; i++) cache.Store(Guid.NewGuid(), new byte[1024]);
        cache.CompactForTesting(0.5);

        WaitForEvictionCount(cache, Microsoft.Extensions.Caching.Memory.EvictionReason.Replaced, minCount: 1);
        WaitForEvictionCount(cache, Microsoft.Extensions.Caching.Memory.EvictionReason.Removed, minCount: 1);
        WaitForEvictionCount(cache, Microsoft.Extensions.Caching.Memory.EvictionReason.Capacity, minCount: 1);
    }

    [TestMethod]
    public void SummaryLog_ResetsHistogramAfterEmit()
    {
        // The 5-min summary line reports a delta per period; running totals
        // would mask recent behaviour as the process ages. After
        // TriggerSummaryForTesting() the per-reason counts must be back to
        // zero so the NEXT period reports only what happened next.
        var cache = CreateTestCache(sizeLimit: 4 * 1024);
        cache.Store(Guid.NewGuid(), new byte[256]);
        var removedToken = Guid.NewGuid();
        cache.Store(removedToken, new byte[256]);
        cache.Remove(removedToken);

        WaitForEvictionCount(cache, Microsoft.Extensions.Caching.Memory.EvictionReason.Removed, minCount: 1);

        cache.TriggerSummaryForTesting();

        Assert.AreEqual(0L,
            cache.GetEvictionCountForTesting(Microsoft.Extensions.Caching.Memory.EvictionReason.Removed),
            "Histogram must be cleared after the summary line emits so each period is a delta.");
    }

    // MemoryCache's PostEvictionCallback dispatches on a ThreadPool worker,
    // so eviction counts aren't observable synchronously after Remove /
    // Replace / Compact. Spin until the bucket reaches `minCount` or a
    // generous timeout — failing the assertion if it never gets there.
    private static void WaitForEvictionCount(
        BlobShareByteCache cache,
        Microsoft.Extensions.Caching.Memory.EvictionReason reason,
        long minCount,
        int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            if (cache.GetEvictionCountForTesting(reason) >= minCount) return;
            Thread.Sleep(10);
        }
        Assert.Fail($"Expected at least {minCount} eviction(s) of reason '{reason}' within {timeoutMs} ms; got {cache.GetEvictionCountForTesting(reason)}.");
    }

    [TestMethod]
    public async Task AccessedEntry_KeepsSlidingWindowOpen()
    {
        // Same 50ms window; a fetch within the window must reset it, so
        // even after total elapsed time exceeds the window the entry stays
        // alive as long as fetches keep coming.
        var cache = CreateTestCache(slidingExpiration: TimeSpan.FromMilliseconds(50));
        var token = Guid.NewGuid();
        cache.Store(token, new byte[16]);

        // Three touches at 25ms intervals = 75ms of total elapsed time but
        // no individual gap exceeds 50ms.
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(25);
            Assert.IsTrue(cache.TryGetBytes(token, out _),
                $"Touch #{i + 1} should refresh the sliding window and find the entry.");
        }
    }

    [TestMethod]
    public async Task ConcurrentSameToken_CollapsesToOneStreamOpenerCall()
    {
        // Single-flight: ten parallel requests for the same token coalesce
        // onto one underlying stream-and-store task. StreamOpener fires
        // exactly once and every response carries the full payload.
        var cache = CreateTestCache();
        var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
        var payload = new byte[IndexedDbBlobChunking.ChunkSize + 11];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 5) & 0xff);

        var openerCalls = 0;
        var gate = new TaskCompletionSource();
        var entry = MakeEntry(payload, streamOpener: async _ =>
        {
            Interlocked.Increment(ref openerCalls);
            // Block briefly so all ten requests definitely race the
            // single-flight insertion before the first one completes.
            await gate.Task.ConfigureAwait(false);
            return new MemoryStream(payload, writable: false);
        });
        registry.Register(entry);

        var ctxs = new (HttpContext ctx, MemoryStream body)[10];
        var tasks = new Task[10];
        for (var i = 0; i < tasks.Length; i++)
        {
            var (ctx, body, _, _) = MakeContext(registry, cache);
            ctxs[i] = (ctx, body);
            tasks[i] = BlobShareEndpoint.HandleAsync(ctx, entry.Token);
        }

        // Give the parallel waves a tick to all reach the gate.
        await Task.Delay(50);
        gate.SetResult();
        await Task.WhenAll(tasks);

        Assert.AreEqual(1, openerCalls,
            "Single-flight must collapse concurrent same-token requests onto one StreamOpener call.");
        foreach (var (ctx, body) in ctxs)
        {
            Assert.AreEqual(StatusCodes.Status200OK, ctx.Response.StatusCode);
            CollectionAssert.AreEqual(payload, body.ToArray());
        }
    }

    [TestMethod]
    public async Task ConcurrentSameScopeDifferentTokens_SerializeAtTheGate()
    {
        // Per-circuit gate: five parallel requests with distinct tokens but
        // the same CircuitScopeId run their StreamOpener bodies one at a
        // time. Without the gate they'd open five concurrent IJSStreamReferences
        // against the same circuit and starve Blazor's data-stream pipe.
        var cache = CreateTestCache();
        var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
        var scopeId = Guid.NewGuid();
        var peakConcurrency = 0;
        var current = 0;

        var entries = new BlobShareEntry[5];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = MakeEntry(
                new byte[64],
                circuitScopeId: scopeId,
                streamOpener: async _ =>
                {
                    var now = Interlocked.Increment(ref current);
                    // Track peak via a CAS loop — interlocked.max isn't on
                    // int directly so we spin until we set a new high.
                    int snapshot;
                    do
                    {
                        snapshot = peakConcurrency;
                        if (now <= snapshot) break;
                    } while (Interlocked.CompareExchange(ref peakConcurrency, now, snapshot) != snapshot);
                    await Task.Delay(20).ConfigureAwait(false);
                    Interlocked.Decrement(ref current);
                    return new MemoryStream(new byte[64], writable: false);
                });
            registry.Register(entries[i]);
        }

        var tasks = new Task[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var (ctx, _, _, _) = MakeContext(registry, cache);
            tasks[i] = BlobShareEndpoint.HandleAsync(ctx, entries[i].Token);
        }
        await Task.WhenAll(tasks);

        Assert.AreEqual(1, peakConcurrency,
            "Per-circuit gate must cap concurrent StreamOpener invocations to 1 within a scope.");
    }

    [TestMethod]
    public async Task DifferentScopes_DoNotSerialize()
    {
        // Sanity check the gate's keying: two scopes, the slow stream on
        // scope A doesn't block the fast stream on scope B from completing.
        var cache = CreateTestCache();
        var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
        var scopeA = Guid.NewGuid();
        var scopeB = Guid.NewGuid();

        var slowGate = new TaskCompletionSource();
        var slow = MakeEntry(
            new byte[16],
            circuitScopeId: scopeA,
            streamOpener: async _ =>
            {
                await slowGate.Task.ConfigureAwait(false);
                return new MemoryStream(new byte[16], writable: false);
            });
        var fast = MakeEntry(new byte[16], circuitScopeId: scopeB);
        registry.Register(slow);
        registry.Register(fast);

        var (slowCtx, _, _, _) = MakeContext(registry, cache);
        var (fastCtx, _, _, _) = MakeContext(registry, cache);

        var slowTask = BlobShareEndpoint.HandleAsync(slowCtx, slow.Token);
        var fastTask = BlobShareEndpoint.HandleAsync(fastCtx, fast.Token);

        // The fast request must complete despite the slow one still holding
        // its (separate) scope's gate.
        var completed = await Task.WhenAny(fastTask, Task.Delay(2000));
        Assert.AreSame(fastTask, completed, "Fast request on a different scope must not be blocked by the slow one.");
        Assert.AreEqual(StatusCodes.Status200OK, fastCtx.Response.StatusCode);

        slowGate.SetResult();
        await slowTask;
    }

    [TestMethod]
    public async Task WatchdogElapses_Returns_503_WithoutPropagatingTimeout()
    {
        // Critical: when the upstream JS stream never yields, OUR watchdog
        // must cancel the read before Blazor's internal pipe timeout would
        // escalate to a fatal circuit exception. The handler returns 503
        // and the cancellation surfaces to the StreamOpener as an
        // OperationCanceledException via its CT.
        //
        // Uses a tiny watchdog wouldn't be possible without test-only
        // surface; instead we rely on the production PerStreamTimeout and
        // assert observable behaviour with a fake stream that never reads
        // until cancelled. The test takes ~PerStreamTimeout (45 s by
        // default) — we shorten it for the test run by setting a low value
        // through a test-only override. See ResetForTests below.
        BlobShareEndpoint.OverrideTimeoutForTesting(TimeSpan.FromMilliseconds(150));
        try
        {
            var cache = CreateTestCache();
            var registry = new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance, cache);
            var observedCancellation = false;
            var entry = MakeEntry(new byte[16], streamOpener: async opCt =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, opCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    observedCancellation = true;
                    throw;
                }
                return new MemoryStream();
            });
            registry.Register(entry);

            var (ctx, body, _, _) = MakeContext(registry, cache);
            await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode,
                "Watchdog timeout must return 503 so the display view's onerror swaps in a placeholder.");
            Assert.AreEqual(0, body.Length);
            Assert.IsTrue(observedCancellation,
                "Watchdog CT must propagate cancellation to the StreamOpener so the JS stream actually unwinds.");
        }
        finally
        {
            BlobShareEndpoint.OverrideTimeoutForTesting(null);
        }
    }

    // Yields exactly one chunk-sized block, then throws JSDisconnectedException
    // on the next read. Used to exercise the mid-stream disconnect path.
    private sealed class ThrowAfterFirstChunkStream : Stream
    {
        private readonly int _firstChunkSize;
        private int _bytesYielded;

        public ThrowAfterFirstChunkStream(int firstChunkSize) { _firstChunkSize = firstChunkSize; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _firstChunkSize * 3;
        public override long Position { get => _bytesYielded; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_bytesYielded < _firstChunkSize)
            {
                var take = Math.Min(buffer.Length, _firstChunkSize - _bytesYielded);
                buffer.Span[..take].Clear();
                _bytesYielded += take;
                return ValueTask.FromResult(take);
            }
            throw new JSDisconnectedException("dropped mid-stream");
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
