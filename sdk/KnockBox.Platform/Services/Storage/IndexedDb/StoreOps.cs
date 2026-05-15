using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Shared op helpers used by every store flavor (typed, JSON, blob). Each helper
/// gates on <see cref="IndexedDbTransaction.IsActive"/> and converts envelope
/// responses into the contract-typed result types.
/// </summary>
internal static class StoreOps
{
    public static async ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
        ITxContext tx, string storeName,
        KeyRange? range, int? count, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<IReadOnlyList<IndexedDbKey>>();
        var raw = await tx.Interop.InvokeRawAsync(
            "storeGetAllKeys", ct, tx.TxId, storeName,
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
                $"Failed to parse key envelopes from store '{storeName}': {ex.Message}");
        }
    }

    public static async ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
        ITxContext tx, string storeName, KeyRange? range, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<long>();
        var raw = await tx.Interop.InvokeRawAsync(
            "storeCount", ct, tx.TxId, storeName,
            IndexedDbWireFormat.ToRangeEnvelope(range)).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<long, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        return element.GetInt64();
    }

    public static async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddOrPutAsync(
        ITxContext tx, string storeName, string method,
        JsonElement payload, IndexedDbKey? key, CancellationToken ct)
    {
        var raw = await tx.Interop.InvokeRawAsync(
            method, ct, tx.TxId, storeName, payload,
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
                $"Failed to parse effective key from {method} on store '{storeName}': {ex.Message}");
        }
    }

    public static async ValueTask<Result<IndexedDbError>> DeleteAsync(
        ITxContext tx, string storeName, IndexedDbKey key, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.NoValue();
        return await tx.Interop.InvokeVoidAsync(
            "storeDelete", ct, tx.TxId, storeName,
            IndexedDbWireFormat.ToKeyEnvelope(key)).ConfigureAwait(false);
    }

    public static async ValueTask<Result<IndexedDbError>> DeleteRangeAsync(
        ITxContext tx, string storeName, KeyRange range, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.NoValue();
        return await tx.Interop.InvokeVoidAsync(
            "storeDeleteRange", ct, tx.TxId, storeName,
            IndexedDbWireFormat.ToRangeEnvelope(range)).ConfigureAwait(false);
    }

    public static async ValueTask<Result<IndexedDbError>> ClearAsync(
        ITxContext tx, string storeName, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.NoValue();
        return await tx.Interop.InvokeVoidAsync(
            "storeClear", ct, tx.TxId, storeName).ConfigureAwait(false);
    }
}

internal static class TxInactive
{
    private static readonly IndexedDbError InactiveError = new(
        IndexedDbErrorKind.TransactionInactive,
        "Transaction is no longer active. Begin a new transaction before issuing operations.");

    public static ValueResult<T, IndexedDbError> Value<T>() => InactiveError;
    public static Result<IndexedDbError> NoValue() => InactiveError;
}
