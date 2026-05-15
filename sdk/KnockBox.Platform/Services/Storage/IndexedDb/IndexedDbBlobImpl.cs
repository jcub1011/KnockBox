using System.Text.Json.Serialization;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed record BlobCreateResponse(
    [property: JsonPropertyName("blobId")] int BlobId,
    [property: JsonPropertyName("length")] long Length);

internal sealed record BlobStreamBeginResponse(
    [property: JsonPropertyName("uploadId")] int UploadId);

internal sealed record BlobChunkResponse(
    [property: JsonPropertyName("base64")] string Base64);

internal sealed record BlobUrlResponse(
    [property: JsonPropertyName("url")] string Url);

internal sealed record BlobPrepareReadResponse(
    [property: JsonPropertyName("length")] long Length,
    [property: JsonPropertyName("contentType")] string ContentType);

internal sealed class IndexedDbBlobImpl : IndexedDbBlob
{
    private readonly IndexedDbInterop _interop;
    private readonly ILogger<IndexedDbBlobImpl> _logger;
    private readonly int _blobId;
    private readonly string _contentType;
    private readonly long _length;
    private string? _cachedObjectUrl;
    private bool _readPrepared;
    private bool _disposed;

    public override string ContentType => _contentType;
    public override long Length => _length;

    internal int BlobId => _blobId;

    public IndexedDbBlobImpl(
        IndexedDbInterop interop,
        ILogger<IndexedDbBlobImpl> logger,
        int blobId,
        string contentType,
        long length)
    {
        _interop = interop;
        _logger = logger;
        _blobId = blobId;
        _contentType = contentType;
        _length = length;
    }

    public override async ValueTask<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureReadPreparedAsync(ct).ConfigureAwait(false);
        var buffer = new byte[_length];
        long offset = 0;
        while (offset < _length)
        {
            var requested = (int)Math.Min(IndexedDbBlobChunking.ChunkSize, _length - offset);
            var chunk = await ReadChunkAsync(offset, requested, ct).ConfigureAwait(false);
            chunk.AsSpan().CopyTo(buffer.AsSpan((int)offset));
            offset += chunk.Length;
            if (chunk.Length == 0)
                throw new IOException($"blobReadChunk returned 0 bytes at offset {offset} of {_length}.");
        }
        return buffer;
    }

    public override async ValueTask<Stream> OpenReadAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureReadPreparedAsync(ct).ConfigureAwait(false);
        return new IndexedDbReadStream(_interop, _blobId, _length);
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

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var result = await _interop.InvokeVoidAsync(
            "releaseHandle", CancellationToken.None, _blobId).ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogWarning(
                "releaseHandle for blob {BlobId} returned [{Kind}] {Message}.",
                _blobId, err.Kind, err.Message);
        }
    }

    private async ValueTask EnsureReadPreparedAsync(CancellationToken ct)
    {
        if (_readPrepared) return;
        var result = await _interop.InvokeAsync<BlobPrepareReadResponse>(
            "blobPrepareRead", ct, _blobId).ConfigureAwait(false);
        if (!result.TryGetSuccess(out _))
        {
            var msg = result.IsCanceled
                ? "Read prepare was canceled."
                : $"[{result.Error.Error.Kind}] {result.Error.Error.Message}";
            throw new IOException("blobPrepareRead failed: " + msg);
        }
        _readPrepared = true;
    }

    internal async ValueTask<byte[]> ReadChunkAsync(long offset, int count, CancellationToken ct)
    {
        if (!_readPrepared) await EnsureReadPreparedAsync(ct).ConfigureAwait(false);
        var result = await _interop.InvokeAsync<BlobChunkResponse>(
            "blobReadChunk", ct, _blobId, offset, count).ConfigureAwait(false);
        if (!result.TryGetSuccess(out var chunk))
        {
            var msg = result.IsCanceled
                ? "Chunk read was canceled."
                : $"[{result.Error.Error.Kind}] {result.Error.Error.Message}";
            throw new IOException("blobReadChunk failed: " + msg);
        }
        return Convert.FromBase64String(chunk.Base64);
    }
}

internal static class IndexedDbBlobChunking
{
    /// <summary>
    /// 16 KB raw bytes per chunk. After base64 expansion (~33%) and JSON
    /// envelope framing this stays comfortably under SignalR's default
    /// MaximumReceiveMessageSize of 32 KB. The host project does not raise
    /// that default (verified at planning time).
    /// </summary>
    public const int ChunkSize = 16 * 1024;
}
