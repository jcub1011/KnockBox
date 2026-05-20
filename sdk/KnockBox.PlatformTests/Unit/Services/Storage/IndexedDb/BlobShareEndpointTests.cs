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
    private static (HttpContext ctx, MemoryStream body, BlobShareRegistry registry) MakeContext(BlobShareRegistry? sharedRegistry = null)
    {
        var registry = sharedRegistry ?? new BlobShareRegistry(NullLogger<BlobShareRegistry>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton<ILogger<BlobShareRegistry>>(NullLogger<BlobShareRegistry>.Instance);
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = provider };
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body, registry);
    }

    // The new BlobShareEntry exposes a StreamOpener: one call yields a single
    // Stream that the endpoint copies into the response. Tests inject a
    // backing byte[] (wrapped in MemoryStream) or a throwing opener.
    private static BlobShareEntry MakeEntry(
        byte[] payload,
        string contentType = "application/octet-stream",
        Func<CancellationToken, ValueTask<Stream>>? streamOpener = null,
        string? cacheControl = null)
        => new()
        {
            Token = Guid.NewGuid(),
            ContentType = contentType,
            Length = payload.LongLength,
            CacheControl = cacheControl,
            StreamOpener = streamOpener
                ?? (_ => ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false))),
        };

    [TestMethod]
    public async Task UnknownToken_Returns_404()
    {
        var (ctx, _, _) = MakeContext();

        await BlobShareEndpoint.HandleAsync(ctx, Guid.NewGuid());

        Assert.AreEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task KnownToken_StreamsFullBody_AndSetsHeaders()
    {
        var (ctx, body, registry) = MakeContext();
        var payload = new byte[IndexedDbBlobChunking.ChunkSize * 2 + 7];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xff);
        var entry = MakeEntry(payload, "image/png");
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.AreEqual("image/png", ctx.Response.ContentType);
        Assert.AreEqual(payload.LongLength, ctx.Response.ContentLength);
        Assert.AreEqual("nosniff", ctx.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.AreEqual("no-store, private", ctx.Response.Headers.CacheControl.ToString());
        CollectionAssert.AreEqual(payload, body.ToArray());
    }

    [TestMethod]
    public async Task CustomCacheControl_IsApplied()
    {
        var (ctx, _, registry) = MakeContext();
        var entry = MakeEntry(Encoding.UTF8.GetBytes("hi"), cacheControl: "public, max-age=60");
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual("public, max-age=60", ctx.Response.Headers.CacheControl.ToString());
    }

    [TestMethod]
    public async Task JsDisconnectedException_BeforeAnyBytes_Returns_410_AndEvicts()
    {
        var (ctx, _, registry) = MakeContext();
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
        var (ctx, _, registry) = MakeContext();
        var entry = MakeEntry(
            new byte[64],
            streamOpener: _ => throw new IOException("boom"));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task MidStream_Disconnect_AbortsWithoutChangingStatus()
    {
        var (ctx, body, registry) = MakeContext();
        // A stream that yields one full ChunkSize block then throws on the
        // next ReadAsync. CopyToAsync will surface the JSDisconnectedException
        // mid-copy after the first chunk lands in the response body.
        var entry = MakeEntry(
            new byte[IndexedDbBlobChunking.ChunkSize * 3],
            streamOpener: _ => ValueTask.FromResult<Stream>(
                new ThrowAfterFirstChunkStream(IndexedDbBlobChunking.ChunkSize)));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        // First chunk made it through, so the 200 status is already on the
        // wire — the endpoint can't flip to 410. The share is still evicted.
        Assert.AreEqual(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.AreEqual(IndexedDbBlobChunking.ChunkSize, body.Length);
        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
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
