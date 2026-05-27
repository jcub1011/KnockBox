using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed record BlobCreateResponse(
    [property: JsonPropertyName("blobId")] int BlobId,
    [property: JsonPropertyName("length")] long Length);

internal sealed record BlobUrlResponse(
    [property: JsonPropertyName("url")] string Url);

internal sealed class IndexedDbBlobImpl : IndexedDbBlob
{
    private readonly IndexedDbInterop _interop;
    private readonly ILogger<IndexedDbBlobImpl> _logger;
    private readonly BlobShareRegistry _shareRegistry;
    private readonly int _blobId;
    private readonly string _contentType;
    private readonly long _length;
    private readonly ConcurrentBag<Guid> _publishedShares = new();
    private string? _cachedObjectUrl;
    private bool _disposed;

    public override string ContentType => _contentType;
    public override long Length => _length;

    internal int BlobId => _blobId;

    public IndexedDbBlobImpl(
        IndexedDbInterop interop,
        ILogger<IndexedDbBlobImpl> logger,
        BlobShareRegistry shareRegistry,
        int blobId,
        string contentType,
        long length)
    {
        _interop = interop;
        _logger = logger;
        _shareRegistry = shareRegistry;
        _blobId = blobId;
        _contentType = contentType;
        _length = length;
    }

    public override async ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var stream = await OpenReadStreamAsync(_length, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(capacity: (int)Math.Min(_length, int.MaxValue));
        await stream.CopyToAsync(buffer, IndexedDbBlobChunking.ChunkSize, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public override async ValueTask<Stream> OpenReadAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await OpenReadStreamAsync(_length, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a binary read stream over SignalR for this blob. Blazor frames
    /// the bytes natively (no base64, no JSON envelope).
    /// <para>
    /// CRITICAL: the returned stream WRAPS the <see cref="IJSStreamReference"/>
    /// so its lifetime is bound to the stream's. Without the wrapper, the
    /// streamRef goes out of scope as soon as this method returns and becomes
    /// eligible for finalization. The finalizer calls
    /// <c>DotNet.jsCallDispatcher.disposeJSObjectReferenceById</c> on the JS
    /// side, which yanks the Blob from <c>_jsObjectReferences</c> — once that
    /// happens the underlying <c>RemoteJSDataStream</c> hangs forever waiting
    /// for bytes that will never come (manifests as
    /// <see cref="TimeoutException"/> "Did not receive any data in the
    /// allotted time" after 60 s, which Blazor escalates to a fatal circuit
    /// error). Holding the streamRef inside the wrapper keeps it GC-rooted
    /// for the entire <c>CopyToAsync</c>.
    /// </para>
    /// </summary>
    internal async ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize, CancellationToken ct)
    {
        var streamRef = await _interop.InvokeStreamRefAsync(
            "openBlobReadStream", ct, _blobId).ConfigureAwait(false);
        var inner = await streamRef.OpenReadStreamAsync(maxAllowedSize, ct).ConfigureAwait(false);
        return new StreamRefBoundStream(inner, streamRef);
    }

    /// <summary>
    /// Pass-through Stream that keeps an <see cref="IJSStreamReference"/>
    /// strongly referenced for the lifetime of the read. Disposing the
    /// wrapper disposes both the inner stream and the streamRef (the latter
    /// releases the JS-side Blob handle so memory doesn't leak).
    /// </summary>
    private sealed class StreamRefBoundStream : Stream
    {
        private readonly Stream _inner;
        private readonly IJSStreamReference _streamRef;
        private bool _disposed;

        public StreamRefBoundStream(Stream inner, IJSStreamReference streamRef)
        {
            _inner = inner;
            _streamRef = streamRef;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => _inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => _inner.ReadAsync(buffer, ct);

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _inner.DisposeAsync().ConfigureAwait(false);
            await _streamRef.DisposeAsync().ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                _inner.Dispose();
                // IJSStreamReference is IAsyncDisposable only — fire-and-forget
                // sync disposal is the best we can do in the sync Dispose path.
                _ = _streamRef.DisposeAsync().AsTask();
            }
            base.Dispose(disposing);
        }
    }

    public override async ValueTask<string> CreateObjectUrlAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cachedObjectUrl is not null) return _cachedObjectUrl;
        var result = await _interop.InvokeAsync<BlobUrlResponse>(
            "blobCreateObjectUrl", ct, _blobId).ConfigureAwait(false);
        if (!result.TryGetSuccess(out var resp))
        {
            var msg = result.IsCanceled
                ? "Object URL creation was canceled."
                : $"[{result.Error.Error.Kind}] {result.Error.Error.Message}";
            _logger.LogError("blobCreateObjectUrl({BlobId}) failed: {Message}", _blobId, msg);
            throw new IOException("blobCreateObjectUrl failed: " + msg);
        }
        _cachedObjectUrl = resp.Url;
        return _cachedObjectUrl;
    }

    public override ValueTask<IBlobShare> PublishForSharingAsync(
        BlobShareOptions? options = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var token = Guid.NewGuid();
        var absoluteExpiry = options?.AbsoluteExpiry is { } abs
            ? DateTimeOffset.UtcNow.Add(abs)
            : (DateTimeOffset?)null;

        var entry = new BlobShareEntry
        {
            Token = token,
            ContentType = _contentType,
            Length = _length,
            CacheControl = options?.CacheControl,
            AbsoluteExpiresAt = absoluteExpiry,
            SlidingExpiry = options?.SlidingExpiry,
            // The interop is scoped per Blazor circuit, so its ScopeId
            // identifies the originating circuit. BlobShareEndpoint uses
            // it to gate concurrent JS-stream opens per circuit (one at a
            // time) so a display view's parallel image fetches don't fan
            // out N simultaneous streams that starve the JS dispatcher
            // past Blazor's pipe-read timeout — which used to escalate
            // to a fatal CircuitHost UnhandledException.
            CircuitScopeId = _interop.ScopeId,
            // Capture `this` (the blob impl) via the lambda so the registry
            // entry can open a fresh SignalR-framed binary stream against
            // the originating circuit's blob each time a player fetches.
            StreamOpener = openCt => OpenReadStreamAsync(_length, openCt),
        };
        _shareRegistry.Register(entry);
        _publishedShares.Add(token);

        var url = $"/blob-share/{token:D}";
        IBlobShare share = new BlobShare(_shareRegistry, token, url, _contentType, _length);
        return ValueTask.FromResult(share);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Revoke any outstanding shares this blob backs — the fetcher closure
        // captures `this`, so once we're disposed the share would otherwise
        // serve from a blob whose underlying JS handle is gone.
        foreach (var token in _publishedShares)
        {
            _shareRegistry.Remove(token);
        }
        var result = await _interop.InvokeVoidAsync(
            "releaseHandle", CancellationToken.None, _blobId).ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogWarning(
                "releaseHandle for blob {BlobId} returned [{Kind}] {Message}.",
                _blobId, err.Kind, err.Message);
        }
    }

}

internal static class IndexedDbBlobChunking
{
    /// <summary>
    /// 64 KB buffer size used as the local copy-buffer when streaming blob
    /// bytes through the SignalR-backed IJSStreamReference pipeline. Native
    /// binary framing (no base64 expansion, no JSON envelope) means we can
    /// go above the legacy 16 KB cap without per-message overhead becoming
    /// the bottleneck; paired with the host's
    /// <c>HubOptions.MaximumReceiveMessageSize = 64 KB</c> the cap stays
    /// defensive against runaway per-message memory while still keeping
    /// chunk overhead amortized.
    /// </summary>
    public const int ChunkSize = 64 * 1024;
}
