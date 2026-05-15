namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Forward-only read stream backed by chunked
/// <c>blobReadChunk</c> JS calls. <see cref="OpenReadAsync"/> on
/// <see cref="IndexedDbBlobImpl"/> hands out fresh instances; disposing the
/// stream does NOT dispose the blob (per the abstract base's contract).
/// </summary>
internal sealed class IndexedDbReadStream : Stream
{
    private readonly IndexedDbInterop _interop;
    private readonly int _blobId;
    private readonly long _length;
    private long _position;
    private bool _disposed;

    public IndexedDbReadStream(IndexedDbInterop interop, int blobId, long length)
    {
        _interop = interop;
        _blobId = blobId;
        _length = length;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("IndexedDbReadStream is forward-only.");
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Synchronous Read is not supported; use ReadAsync.");

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_position >= _length) return 0;
        var requested = (int)Math.Min(Math.Min(count, IndexedDbBlobChunking.ChunkSize), _length - _position);
        var bytes = await FetchChunkAsync(requested, ct).ConfigureAwait(false);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
        return bytes.Length;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_position >= _length) return 0;
        var requested = (int)Math.Min(Math.Min(buffer.Length, IndexedDbBlobChunking.ChunkSize), _length - _position);
        var bytes = await FetchChunkAsync(requested, ct).ConfigureAwait(false);
        bytes.AsSpan().CopyTo(buffer.Span);
        return bytes.Length;
    }

    private async ValueTask<byte[]> FetchChunkAsync(int count, CancellationToken ct)
    {
        var result = await _interop.InvokeAsync<BlobChunkResponse>(
            "blobReadChunk", ct, _blobId, _position, count).ConfigureAwait(false);
        if (!result.TryGetSuccess(out var chunk))
        {
            var msg = result.IsCanceled
                ? "Chunk read was canceled."
                : $"[{result.Error.Error.Kind}] {result.Error.Error.Message}";
            throw new IOException("blobReadChunk failed: " + msg);
        }
        var bytes = Convert.FromBase64String(chunk.Base64);
        _position += bytes.Length;
        return bytes;
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException("IndexedDbReadStream is forward-only.");
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
