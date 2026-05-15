using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Shared cursor-open path used by stores and indexes. The JS module returns
/// the first entry inline with the cursor handle so the round-trip cost of
/// the open amortizes — empty cursors come back with <c>HasFirst = false</c>
/// and <c>CursorId = null</c> and the wrapper produces an empty enumerator.
/// </summary>
internal static class CursorOpen
{
    /// <param name="indexName"><see langword="null"/> for a store-level cursor;
    /// the index name for an index-level cursor.</param>
    public static async ValueTask<ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>> OpenValueAsync<TValue>(
        ITxContext tx, string storeName, string? indexName,
        KeyRange? range, CursorDirection direction, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<IIndexedDbCursor<TValue>>();
        var raw = await tx.Interop.InvokeAsync<CursorOpenResponse>(
            "openCursor", ct, tx.TxId, storeName, indexName,
            IndexedDbWireFormat.ToRangeEnvelope(range), (int)direction, "value")
            .ConfigureAwait(false);
        if (!raw.TryGetSuccess(out var resp))
        {
            if (raw.IsCanceled) return ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>.Canceled;
            return raw.Error.Error;
        }
        if (!resp.HasFirst || resp.CursorId is null)
        {
            return ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>.FromValue(
                new IndexedDbCursor<TValue>(tx, cursorId: -1, firstEntry: null));
        }
        var firstEntry = IndexedDbCursor<TValue>.ParseEntry(resp.Entry!.Value, tx.JsonOptions);
        return ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>.FromValue(
            new IndexedDbCursor<TValue>(tx, resp.CursorId.Value, firstEntry));
    }

    public static async ValueTask<ValueResult<IJsonObjectCursor, IndexedDbError>> OpenJsonAsync(
        ITxContext tx, string storeName, string? indexName,
        KeyRange? range, CursorDirection direction, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<IJsonObjectCursor>();
        var raw = await tx.Interop.InvokeAsync<CursorOpenResponse>(
            "openCursor", ct, tx.TxId, storeName, indexName,
            IndexedDbWireFormat.ToRangeEnvelope(range), (int)direction, "value")
            .ConfigureAwait(false);
        if (!raw.TryGetSuccess(out var resp))
        {
            if (raw.IsCanceled) return ValueResult<IJsonObjectCursor, IndexedDbError>.Canceled;
            return raw.Error.Error;
        }
        if (!resp.HasFirst || resp.CursorId is null)
        {
            return ValueResult<IJsonObjectCursor, IndexedDbError>.FromValue(
                new JsonObjectCursor(tx, cursorId: -1, firstEntry: null));
        }
        var first = JsonObjectCursor.ParseEntry(resp.Entry!.Value);
        return ValueResult<IJsonObjectCursor, IndexedDbError>.FromValue(
            new JsonObjectCursor(tx, resp.CursorId.Value, first));
    }

    public static async ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyAsync(
        ITxContext tx, string storeName, string? indexName,
        KeyRange? range, CursorDirection direction, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<IIndexedDbKeyCursor>();
        var raw = await tx.Interop.InvokeAsync<CursorOpenResponse>(
            "openCursor", ct, tx.TxId, storeName, indexName,
            IndexedDbWireFormat.ToRangeEnvelope(range), (int)direction, "keyOnly")
            .ConfigureAwait(false);
        if (!raw.TryGetSuccess(out var resp))
        {
            if (raw.IsCanceled) return ValueResult<IIndexedDbKeyCursor, IndexedDbError>.Canceled;
            return raw.Error.Error;
        }
        if (!resp.HasFirst || resp.CursorId is null)
        {
            return ValueResult<IIndexedDbKeyCursor, IndexedDbError>.FromValue(
                new IndexedDbKeyCursor(tx, cursorId: -1, firstEntry: null));
        }
        var first = IndexedDbKeyCursor.ParseEntry(resp.Entry!.Value);
        return ValueResult<IIndexedDbKeyCursor, IndexedDbError>.FromValue(
            new IndexedDbKeyCursor(tx, resp.CursorId.Value, first));
    }
}
