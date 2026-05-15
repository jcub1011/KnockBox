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

    private static BlobShareEntry MakeEntry(
        byte[] payload,
        string contentType = "application/octet-stream",
        Func<long, int, CancellationToken, ValueTask<byte[]>>? fetcher = null,
        string? cacheControl = null)
        => new()
        {
            Token = Guid.NewGuid(),
            ContentType = contentType,
            Length = payload.LongLength,
            CacheControl = cacheControl,
            Fetcher = fetcher ?? ((offset, count, ct) =>
            {
                var take = (int)Math.Min(count, payload.LongLength - offset);
                var slice = new byte[take];
                Array.Copy(payload, offset, slice, 0, take);
                return ValueTask.FromResult(slice);
            }),
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
    public async Task JsDisconnectedException_OnFirstChunk_Returns_410_AndEvicts()
    {
        var (ctx, _, registry) = MakeContext();
        var entry = MakeEntry(
            new byte[64],
            fetcher: (offset, count, ct) => throw new JSDisconnectedException("circuit gone"));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status410Gone, ctx.Response.StatusCode);
        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public async Task UnexpectedException_OnFirstChunk_Returns_500()
    {
        var (ctx, _, registry) = MakeContext();
        var entry = MakeEntry(
            new byte[64],
            fetcher: (offset, count, ct) => throw new IOException("boom"));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task MidStream_Disconnect_AbortsWithoutChangingStatus()
    {
        var (ctx, body, registry) = MakeContext();
        var callCount = 0;
        var entry = MakeEntry(
            new byte[IndexedDbBlobChunking.ChunkSize * 3],
            fetcher: (offset, count, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return ValueTask.FromResult(new byte[count]);
                }
                throw new JSDisconnectedException("dropped mid-stream");
            });
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        // First chunk was successfully streamed before the disconnect, so the
        // 200 status line is already on the wire — status code is not flipped
        // to 410. Body got partially written.
        Assert.AreEqual(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.AreEqual(IndexedDbBlobChunking.ChunkSize, body.Length);
        Assert.IsNull(registry.TryGetAndTouch(entry.Token));
    }

    [TestMethod]
    public async Task FetcherReturning_ZeroBytes_AbortsWithoutThrowing()
    {
        var (ctx, body, registry) = MakeContext();
        var entry = MakeEntry(
            new byte[128],
            fetcher: (offset, count, ct) => ValueTask.FromResult(Array.Empty<byte>()));
        registry.Register(entry);

        await BlobShareEndpoint.HandleAsync(ctx, entry.Token);

        Assert.AreEqual(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.AreEqual(0, body.Length);
    }
}
