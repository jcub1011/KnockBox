using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class TypedIndex<TValue> : IIndex<TValue>
{
    private readonly ITxContext _tx;
    private readonly string _storeName;

    public string Name { get; }
    public KeyPath KeyPath { get; }
    public bool Unique { get; }
    public bool MultiEntry { get; }

    public TypedIndex(ITxContext tx, string storeName, string indexName, IndexSchema schema)
    {
        _tx = tx;
        _storeName = storeName;
        Name = indexName;
        KeyPath = schema.KeyPath.Length == 1
            ? KeyPath.Single(schema.KeyPath[0])
            : KeyPath.Composite(schema.KeyPath);
        Unique = schema.Unique;
        MultiEntry = schema.MultiEntry;
    }

    public async ValueTask<ValueResult<TValue?, IndexedDbError>> GetAsync(
        IndexedDbKey key, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<TValue?>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "indexGet", ct, _tx.TxId, _storeName, Name,
            IndexedDbWireFormat.ToKeyEnvelope(key)).ConfigureAwait(false);
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
                $"Failed to deserialize from index '{Name}': {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<IReadOnlyList<TValue>, IndexedDbError>> GetAllAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IReadOnlyList<TValue>>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "indexGetAll", ct, _tx.TxId, _storeName, Name,
            IndexedDbWireFormat.ToRangeEnvelope(range), count).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IReadOnlyList<TValue>, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try
        {
            var list = new List<TValue>();
            foreach (var item in element.EnumerateArray())
                list.Add(item.Deserialize<TValue>(_tx.JsonOptions)!);
            return ValueResult<IReadOnlyList<TValue>, IndexedDbError>.FromValue(list);
        }
        catch (JsonException ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to deserialize GetAll values from index '{Name}': {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
        KeyRange? range = null, int? count = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<IReadOnlyList<IndexedDbKey>>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "indexGetAllKeys", ct, _tx.TxId, _storeName, Name,
            IndexedDbWireFormat.ToRangeEnvelope(range), count).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try
        {
            var keys = new List<IndexedDbKey>();
            foreach (var k in element.EnumerateArray())
                keys.Add(IndexedDbWireFormat.FromKeyEnvelope(k));
            return ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.FromValue(keys);
        }
        catch (Exception ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to parse key envelopes from index '{Name}': {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
        KeyRange? range = null, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<long>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "indexCount", ct, _tx.TxId, _storeName, Name,
            IndexedDbWireFormat.ToRangeEnvelope(range)).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<long, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        return element.GetInt64();
    }

    public ValueTask<ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>> OpenCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenValueAsync<TValue>(_tx, _storeName, Name, range, direction, ct);

    public ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenKeyAsync(_tx, _storeName, Name, range, direction, ct);
}
