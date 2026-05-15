using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class ObjectStore<TValue> : IObjectStore<TValue>
{
    private readonly ITxContext _tx;

    public string Name { get; }

    public ObjectStore(ITxContext tx, string name)
    {
        _tx = tx;
        Name = name;
    }

    public async ValueTask<ValueResult<TValue?, IndexedDbError>> GetAsync(
        IndexedDbKey key, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<TValue?>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "storeGet", ct, _tx.TxId, Name, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<TValue?, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueResult<TValue?, IndexedDbError>.FromValue(default);
        try
        {
            return ValueResult<TValue?, IndexedDbError>.FromValue(
                element.Deserialize<TValue>(_tx.JsonOptions));
        }
        catch (JsonException ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to deserialize value from store '{Name}': {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<IReadOnlyList<TValue>, IndexedDbError>> GetAllAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IReadOnlyList<TValue>>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "storeGetAll", ct, _tx.TxId, Name,
            IndexedDbWireFormat.ToRangeEnvelope(range), count).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IReadOnlyList<TValue>, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try
        {
            var list = new List<TValue>();
            foreach (var entry in element.EnumerateArray())
            {
                list.Add(entry.Deserialize<TValue>(_tx.JsonOptions)!);
            }
            return ValueResult<IReadOnlyList<TValue>, IndexedDbError>.FromValue(list);
        }
        catch (JsonException ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to deserialize GetAll values from store '{Name}': {ex.Message}");
        }
    }

    public ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
        => StoreOps.GetAllKeysAsync(_tx, Name, range, count, ct);

    public ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
        KeyRange? range = null, CancellationToken ct = default)
        => StoreOps.CountAsync(_tx, Name, range, ct);

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddAsync(
        TValue value, IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IndexedDbKey>();
        var payload = JsonSerializer.SerializeToElement(value, _tx.JsonOptions);
        return await StoreOps.AddOrPutAsync(_tx, Name, "storeAdd", payload, key, ct).ConfigureAwait(false);
    }

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> PutAsync(
        TValue value, IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IndexedDbKey>();
        var payload = JsonSerializer.SerializeToElement(value, _tx.JsonOptions);
        return await StoreOps.AddOrPutAsync(_tx, Name, "storePut", payload, key, ct).ConfigureAwait(false);
    }

    public ValueTask<Result<IndexedDbError>> DeleteAsync(IndexedDbKey key, CancellationToken ct = default)
        => StoreOps.DeleteAsync(_tx, Name, key, ct);

    public ValueTask<Result<IndexedDbError>> DeleteRangeAsync(KeyRange range, CancellationToken ct = default)
        => StoreOps.DeleteRangeAsync(_tx, Name, range, ct);

    public ValueTask<Result<IndexedDbError>> ClearAsync(CancellationToken ct = default)
        => StoreOps.ClearAsync(_tx, Name, ct);

    public IIndex<TValue> Index(string name)
        => throw new NotImplementedException("Indexes land in Phase 3 of the IndexedDB rollout.");

    public ValueTask<ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>> OpenCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => throw new NotImplementedException("Cursors land in Phase 3 of the IndexedDB rollout.");

    public ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => throw new NotImplementedException("Cursors land in Phase 3 of the IndexedDB rollout.");
}
