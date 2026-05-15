using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Wire shape returned by <c>cursorContinue</c> / <c>cursorAdvance</c>.
/// <c>Done</c> is <see langword="true"/> when the cursor is exhausted;
/// otherwise <c>Entry</c> contains the next record envelope.
/// </summary>
internal sealed record CursorMoveResponse(
    [property: JsonPropertyName("done")]  bool Done,
    [property: JsonPropertyName("entry")] JsonElement? Entry);

internal sealed record CursorOpenResponse(
    [property: JsonPropertyName("cursorId")] int? CursorId,
    [property: JsonPropertyName("hasFirst")] bool HasFirst,
    [property: JsonPropertyName("entry")]    JsonElement? Entry);

/// <summary>
/// Shared JS-interop calls for every cursor flavor. Each method returns the
/// underlying envelope-wrapped result so the cursor wrapper can convert it
/// into the contract-typed signature.
/// </summary>
internal static class CursorRpc
{
    public static async ValueTask<ValueResult<CursorMoveResponse, IndexedDbError>> ContinueAsync(
        ITxContext tx, int cursorId, IndexedDbKey? key, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<CursorMoveResponse>();
        return await tx.Interop.InvokeAsync<CursorMoveResponse>(
            "cursorContinue", ct, cursorId, IndexedDbWireFormat.ToKeyEnvelope(key)).ConfigureAwait(false);
    }

    public static async ValueTask<ValueResult<CursorMoveResponse, IndexedDbError>> AdvanceAsync(
        ITxContext tx, int cursorId, int count, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.Value<CursorMoveResponse>();
        if (count <= 0)
            return new IndexedDbError(IndexedDbErrorKind.Data, "Cursor advance count must be > 0.");
        return await tx.Interop.InvokeAsync<CursorMoveResponse>(
            "cursorAdvance", ct, cursorId, count).ConfigureAwait(false);
    }

    public static async ValueTask<Result<IndexedDbError>> UpdateAsync(
        ITxContext tx, int cursorId, JsonElement payload, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.NoValue();
        return await tx.Interop.InvokeVoidAsync(
            "cursorUpdate", ct, cursorId, payload).ConfigureAwait(false);
    }

    public static async ValueTask<Result<IndexedDbError>> DeleteAsync(
        ITxContext tx, int cursorId, CancellationToken ct)
    {
        if (!tx.IsActive) return TxInactive.NoValue();
        return await tx.Interop.InvokeVoidAsync("cursorDelete", ct, cursorId).ConfigureAwait(false);
    }

    public static async ValueTask ReleaseAsync(ITxContext tx, int cursorId)
    {
        if (!tx.IsActive) return;
        await tx.Interop.InvokeVoidAsync("releaseHandle", CancellationToken.None, cursorId).ConfigureAwait(false);
    }
}
