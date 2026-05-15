using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class JsonIndex : IIndex
{
    private readonly ITxContext _tx;
    private readonly string _storeName;

    public string Name { get; }
    public KeyPath KeyPath { get; }
    public bool Unique { get; }
    public bool MultiEntry { get; }

    public JsonIndex(ITxContext tx, string storeName, string indexName, IndexSchema schema)
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

    public async ValueTask<ValueResult<JsonElement?, IndexedDbError>> GetAsync(
        IndexedDbKey key, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.Value<JsonElement?>();
        var raw = await _tx.Interop.InvokeRawAsync(
            "indexGet", ct, _tx.TxId, _storeName, Name,
            IndexedDbWireFormat.ToKeyEnvelope(key)).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<JsonElement?, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueResult<JsonElement?, IndexedDbError>.FromValue(null);
        return ValueResult<JsonElement?, IndexedDbError>.FromValue(element.Clone());
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

    public ValueTask<ValueResult<IJsonObjectCursor, IndexedDbError>> OpenCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenJsonAsync(_tx, _storeName, Name, range, direction, ct);

    public ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
        KeyRange? range = null, CursorDirection direction = CursorDirection.Next, CancellationToken ct = default)
        => CursorOpen.OpenKeyAsync(_tx, _storeName, Name, range, direction, ct);
}
