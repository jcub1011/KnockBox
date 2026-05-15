using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDbService : IIndexedDbService, IAsyncDisposable
{
    private readonly IndexedDbInterop _interop;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IndexedDbService> _logger;
    private readonly BlobShareRegistry _shareRegistry;

    public IndexedDbService(
        IJSRuntime jsRuntime,
        ILoggerFactory loggerFactory,
        BlobShareRegistry shareRegistry)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<IndexedDbService>();
        _shareRegistry = shareRegistry;
        _interop = new IndexedDbInterop(jsRuntime, loggerFactory.CreateLogger<IndexedDbInterop>());
    }

    public async ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>> OpenAsync(
        IndexedDbSchema schema, CancellationToken ct = default)
    {
        var bridge = new VersionChangeBridge(_interop, _loggerFactory, _shareRegistry, schema);
        var bridgeRef = DotNetObjectReference.Create(bridge);

        var hasUpgrade = schema.OnUpgrade is not null;
        var result = await _interop.InvokeAsync<OpenDatabaseResponse>(
            "openDatabase", ct, schema.Name, schema.Version, hasUpgrade, bridgeRef)
            .ConfigureAwait(false);

        if (!result.TryGetSuccess(out var resp))
        {
            bridgeRef.Dispose();
            if (result.IsCanceled)
                return ValueResult<IIndexedDatabase, IndexedDbError>.Canceled;
            var err = result.Error.Error;
            _logger.LogError(
                "Opening IndexedDB '{DatabaseName}' v{Version} failed: [{Kind}] {Message} (jsName: {JsName}).",
                schema.Name, schema.Version, err.Kind, err.Message, err.JsName);
            return err;
        }

        var db = new IndexedDatabase(
            _interop,
            _loggerFactory,
            _shareRegistry,
            resp.DbId, schema.Name, resp.Version,
            resp.ObjectStoreNames,
            schema.JsonOptions ?? IndexedDbWireFormat.DefaultJsonOptions,
            resp.Schema ?? new Dictionary<string, StoreSchema>(),
            bridgeRef);
        bridge.AttachDatabase(db);
        return db;
    }

    public async ValueTask<Result<IndexedDbError>> DeleteDatabaseAsync(string name, CancellationToken ct = default)
    {
        var result = await _interop.InvokeVoidAsync("deleteDatabase", ct, name).ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogError(
                "Deleting IndexedDB '{DatabaseName}' failed: [{Kind}] {Message} (jsName: {JsName}).",
                name, err.Kind, err.Message, err.JsName);
        }
        return result;
    }

    public async ValueTask<ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>> ListDatabasesAsync(
        CancellationToken ct = default)
    {
        var result = await _interop.InvokeAsync<ListDatabasesResponse>("listDatabases", ct)
            .ConfigureAwait(false);
        if (!result.TryGetSuccess(out var resp))
        {
            if (result.IsCanceled)
                return ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.Canceled;
            var err = result.Error.Error;
            _logger.LogWarning(
                "Listing IndexedDB databases failed: [{Kind}] {Message} (jsName: {JsName}).",
                err.Kind, err.Message, err.JsName);
            return err;
        }

        IReadOnlyList<DatabaseInfo> infos = resp.Infos
            .Select(i => new DatabaseInfo(i.Name, i.Version))
            .ToList();
        return ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.FromValue(infos);
    }

    public ValueTask<IndexedDbBlob> CreateBlobAsync(
        ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
    {
        // Small payloads (one chunk) take the single-call createBlobFromBytes
        // path; anything larger goes through the chunked stream upload to keep
        // a single SignalR message under the receive limit.
        if (bytes.Length <= IndexedDbBlobChunking.ChunkSize)
        {
            return CreateBlobFromBytesAsync(bytes, contentType, ct);
        }
        // Fall through to the stream path with a backing MemoryStream.
        var arr = bytes.ToArray();
        return CreateBlobAsync(new MemoryStream(arr, writable: false), arr.LongLength, contentType, leaveOpen: false, ct);
    }

    private async ValueTask<IndexedDbBlob> CreateBlobFromBytesAsync(
        ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(bytes.Span);
        var result = await _interop.InvokeAsync<BlobCreateResponse>(
            "createBlobFromBytes", ct, base64, contentType).ConfigureAwait(false);
        if (!result.TryGetSuccess(out var resp))
        {
            var msg = result.IsCanceled
                ? "Blob creation was canceled."
                : $"[{result.Error.Error.Kind}] {result.Error.Error.Message}";
            _logger.LogError("createBlobFromBytes failed: {Message}", msg);
            throw new IOException("createBlobFromBytes failed: " + msg);
        }
        return new IndexedDbBlobImpl(
            _interop,
            _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
            _shareRegistry,
            resp.BlobId, contentType, resp.Length);
    }

    public async ValueTask<IndexedDbBlob> CreateBlobAsync(
        Stream stream, long length, string contentType, bool leaveOpen = false, CancellationToken ct = default)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

        try
        {
            var begin = await _interop.InvokeAsync<BlobStreamBeginResponse>(
                "createBlobStreamBegin", ct, contentType, length).ConfigureAwait(false);
            if (!begin.TryGetSuccess(out var beginResp))
            {
                var msg = begin.IsCanceled
                    ? "Blob stream upload was canceled."
                    : $"[{begin.Error.Error.Kind}] {begin.Error.Error.Message}";
                throw new IOException("createBlobStreamBegin failed: " + msg);
            }

            var uploadId = beginResp.UploadId;
            var buffer = new byte[IndexedDbBlobChunking.ChunkSize];
            long total = 0;
            while (total < length)
            {
                var toRead = (int)Math.Min(buffer.Length, length - total);
                var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException(
                        $"Source stream ended at byte {total} of {length}; expected {length} bytes total.");
                }
                var base64 = Convert.ToBase64String(buffer, 0, read);
                var append = await _interop.InvokeVoidAsync(
                    "createBlobStreamAppend", ct, uploadId, base64).ConfigureAwait(false);
                if (append.TryGetFailure(out var appendErr))
                {
                    throw new IOException(
                        $"createBlobStreamAppend failed: [{appendErr.Kind}] {appendErr.Message}");
                }
                total += read;
            }

            var finish = await _interop.InvokeAsync<BlobCreateResponse>(
                "createBlobStreamFinish", ct, uploadId).ConfigureAwait(false);
            if (!finish.TryGetSuccess(out var finishResp))
            {
                var msg = finish.IsCanceled
                    ? "Blob stream finalization was canceled."
                    : $"[{finish.Error.Error.Kind}] {finish.Error.Error.Message}";
                throw new IOException("createBlobStreamFinish failed: " + msg);
            }
            return new IndexedDbBlobImpl(
                _interop,
                _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
                _shareRegistry,
                finishResp.BlobId, contentType, finishResp.Length);
        }
        finally
        {
            if (!leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync() => _interop.DisposeAsync();
}

internal sealed record OpenDatabaseResponse(
    int DbId,
    int Version,
    IReadOnlyList<string> ObjectStoreNames,
    Dictionary<string, StoreSchema>? Schema);
internal sealed record ListDatabasesResponse(IReadOnlyList<DatabaseInfoEntry> Infos);
internal sealed record DatabaseInfoEntry(string Name, int Version);
