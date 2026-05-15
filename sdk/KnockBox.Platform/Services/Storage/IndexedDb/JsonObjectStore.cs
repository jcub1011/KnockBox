using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class JsonObjectStore : IJsonObjectStore
{
    private readonly ITxContext _tx;

    public string Name { get; }

    public JsonObjectStore(ITxContext tx, string name)
    {
        _tx = tx;
        Name = name;
    }

    public async ValueTask<ValueResult<JsonElement?, IndexedDbError>> GetAsync(
        IndexedDbKey key, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<JsonElement?>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "storeGet", ct, _tx.TxId, Name, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<JsonElement?, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueResult<JsonElement?, IndexedDbError>.FromValue(null);
        return ValueResult<JsonElement?, IndexedDbError>.FromValue(element.Clone());
    }

    public async ValueTask<ValueResult<IReadOnlyList<JsonElement>, IndexedDbError>> GetAllAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IReadOnlyList<JsonElement>>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "storeGetAll", ct, _tx.TxId, Name,
            IndexedDbWireFormat.ToRangeEnvelope(range), count).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IReadOnlyList<JsonElement>, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        var list = new List<JsonElement>();
        foreach (var entry in element.EnumerateArray())
            list.Add(entry.Clone());
        return ValueResult<IReadOnlyList<JsonElement>, IndexedDbError>.FromValue(list);
    }

    public ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
        => StoreOps.GetAllKeysAsync(_tx, Name, range, count, ct);

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddAsync(
        JsonElement value, IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IndexedDbKey>();
        return await StoreOps.AddOrPutAsync(_tx, Name, "storeAdd", value, key, ct).ConfigureAwait(false);
    }

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> PutAsync(
        JsonElement value, IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IndexedDbKey>();
        return await StoreOps.AddOrPutAsync(_tx, Name, "storePut", value, key, ct).ConfigureAwait(false);
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

    public IIndex Index(string name)
    {
        if (!_tx.TryGetIndexSchema(Name, name, out var schema))
        {
            throw new InvalidOperationException(
                $"Index '{name}' is not defined on store '{Name}'. " +
                "Index metadata is captured at database-open time and is not available during an upgrade callback.");
        }
        return new JsonIndex(_tx, Name, name, schema);
    }

    public ValueTask<ValueResult<IJsonObjectCursor, IndexedDbError>> OpenCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenJsonAsync(_tx, Name, indexName: null, range, direction, ct);

    public ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenKeyAsync(_tx, Name, indexName: null, range, direction, ct);
}
