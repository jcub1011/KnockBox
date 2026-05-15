using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class BlobObjectStore : IBlobObjectStore
{
    private readonly ITxContext _tx;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BlobShareRegistry _shareRegistry;

    public string Name { get; }

    public BlobObjectStore(
        ITxContext tx,
        ILoggerFactory loggerFactory,
        BlobShareRegistry shareRegistry,
        string name)
    {
        _tx = tx;
        _loggerFactory = loggerFactory;
        _shareRegistry = shareRegistry;
        Name = name;
    }

    public async ValueTask<ValueResult<IndexedDbBlob?, IndexedDbError>> GetAsync(
        IndexedDbKey key, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IndexedDbBlob?>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "blobStoreGet", ct, _tx.TxId, Name,
            IndexedDbWireFormat.ToKeyEnvelope(key)).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IndexedDbBlob?, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueResult<IndexedDbBlob?, IndexedDbError>.FromValue(null);
        return ValueResult<IndexedDbBlob?, IndexedDbError>.FromValue(ParseBlob(element));
    }

    public ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
        => StoreOps.GetAllKeysAsync(_tx, Name, range, count, ct);

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddAsync(
        IndexedDbBlob blob, IndexedDbKey? key = null, CancellationToken ct = default)
        => await StoreBlobAsync("blobStoreAdd", blob, key, ct).ConfigureAwait(false);

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> PutAsync(
        IndexedDbBlob blob, IndexedDbKey? key = null, CancellationToken ct = default)
        => await StoreBlobAsync("blobStorePut", blob, key, ct).ConfigureAwait(false);

    private async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> StoreBlobAsync(
        string method, IndexedDbBlob blob, IndexedDbKey? key, CancellationToken ct)
    {
        if (!_tx.IsActive) return TxInactive.Value<IndexedDbKey>();
        if (blob is not IndexedDbBlobImpl impl)
        {
            return new IndexedDbError(
                IndexedDbErrorKind.Data,
                "Blob must be one constructed via IIndexedDbService.CreateBlobAsync or read from a blob store.");
        }
        var raw = await _tx.Interop.InvokeRawAsync(
            method, ct, _tx.TxId, Name, impl.BlobId,
            IndexedDbWireFormat.ToKeyEnvelope(key)).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IndexedDbKey, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try
        {
            return IndexedDbWireFormat.FromKeyEnvelope(element);
        }
        catch (Exception ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to parse effective key from {method} on store '{Name}': {ex.Message}");
        }
    }

    public ValueTask<Result<IndexedDbError>> DeleteAsync(IndexedDbKey key, CancellationToken ct = default)
        => StoreOps.DeleteAsync(_tx, Name, key, ct);

    public ValueTask<Result<IndexedDbError>> DeleteRangeAsync(KeyRange range, CancellationToken ct = default)
        => StoreOps.DeleteRangeAsync(_tx, Name, range, ct);

    public ValueTask<Result<IndexedDbError>> ClearAsync(CancellationToken ct = default)
        => StoreOps.ClearAsync(_tx, Name, ct);

    public ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
        KeyRange? range = null, CancellationToken ct = default)
        => StoreOps.CountAsync(_tx, Name, range, ct);

    public async ValueTask<ValueResult<IBlobObjectCursor, IndexedDbError>> OpenCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IBlobObjectCursor>();
        var raw = await _tx.Interop.InvokeAsync<CursorOpenResponse>(
            "openCursor", ct, _tx.TxId, Name, (string?)null,
            IndexedDbWireFormat.ToRangeEnvelope(range), (int)direction, "blob")
            .ConfigureAwait(false);
        if (!raw.TryGetSuccess(out var resp))
        {
            if (raw.IsCanceled) return ValueResult<IBlobObjectCursor, IndexedDbError>.Canceled;
            return raw.Error.Error;
        }
        if (!resp.HasFirst || resp.CursorId is null)
        {
            return ValueResult<IBlobObjectCursor, IndexedDbError>.FromValue(
                new BlobObjectCursor(_tx, _loggerFactory, _shareRegistry, cursorId: -1, firstEntry: null));
        }
        var firstEntry = BlobObjectCursor.ParseEntry(_tx.Interop, _loggerFactory, _shareRegistry, resp.Entry!.Value);
        return ValueResult<IBlobObjectCursor, IndexedDbError>.FromValue(
            new BlobObjectCursor(_tx, _loggerFactory, _shareRegistry, resp.CursorId.Value, firstEntry));
    }

    public ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenKeyAsync(_tx, Name, indexName: null, range, direction, ct);

    internal IndexedDbBlob ParseBlob(JsonElement element)
    {
        var blobId = element.GetProperty("blobId").GetInt32();
        var contentType = element.GetProperty("contentType").GetString() ?? "application/octet-stream";
        var length = element.GetProperty("length").GetInt64();
        return new IndexedDbBlobImpl(
            _tx.Interop,
            _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
            _shareRegistry,
            blobId, contentType, length);
    }
}
