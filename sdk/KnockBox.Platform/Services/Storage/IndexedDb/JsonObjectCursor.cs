using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class JsonObjectCursor
    : IJsonObjectCursor, IAsyncEnumerator<CursorEntry<JsonElement>>
{
    private readonly ITxContext _tx;
    private readonly int _cursorId;
    private readonly bool _hasBufferedFirst;
    private CursorEntry<JsonElement>? _current;
    private bool _firstYielded;
    private bool _disposed;
    private CancellationToken _enumCt;

    public CursorEntry<JsonElement>? Current => _current;

    public JsonObjectCursor(ITxContext tx, int cursorId, CursorEntry<JsonElement>? firstEntry)
    {
        _tx = tx;
        _cursorId = cursorId;
        if (firstEntry.HasValue)
        {
            _current = firstEntry;
            _hasBufferedFirst = true;
        }
    }

    public async ValueTask<bool> MoveNextAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        if (_hasBufferedFirst && !_firstYielded)
        {
            _firstYielded = true;
            return _current.HasValue;
        }
        var step = await CursorRpc.ContinueAsync(_tx, _cursorId, null, ct).ConfigureAwait(false);
        if (!step.TryGetSuccess(out var resp) || resp.Done || resp.Entry is null)
        {
            _current = null;
            return false;
        }
        _current = ParseEntry(resp.Entry.Value);
        return true;
    }

    public async ValueTask<Result<IndexedDbError>> AdvanceAsync(int count, CancellationToken ct = default)
    {
        if (_disposed) return new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "Cursor is disposed.");
        var step = await CursorRpc.AdvanceAsync(_tx, _cursorId, count, ct).ConfigureAwait(false);
        if (!step.TryGetSuccess(out var resp))
        {
            if (step.IsCanceled) return Result<IndexedDbError>.Canceled;
            return step.Error.Error;
        }
        if (resp.Done || resp.Entry is null) { _current = null; return Result<IndexedDbError>.Success; }
        _current = ParseEntry(resp.Entry.Value);
        return Result<IndexedDbError>.Success;
    }

    public async ValueTask<Result<IndexedDbError>> ContinueAsync(
        IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (_disposed) return new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "Cursor is disposed.");
        var step = await CursorRpc.ContinueAsync(_tx, _cursorId, key, ct).ConfigureAwait(false);
        if (!step.TryGetSuccess(out var resp))
        {
            if (step.IsCanceled) return Result<IndexedDbError>.Canceled;
            return step.Error.Error;
        }
        if (resp.Done || resp.Entry is null) { _current = null; return Result<IndexedDbError>.Success; }
        _current = ParseEntry(resp.Entry.Value);
        return Result<IndexedDbError>.Success;
    }

    public ValueTask<Result<IndexedDbError>> UpdateAsync(JsonElement value, CancellationToken ct = default)
        => CursorRpc.UpdateAsync(_tx, _cursorId, value, ct);

    public ValueTask<Result<IndexedDbError>> DeleteAsync(CancellationToken ct = default)
        => CursorRpc.DeleteAsync(_tx, _cursorId, ct);

    public IAsyncEnumerator<CursorEntry<JsonElement>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        _enumCt = cancellationToken;
        return this;
    }

    CursorEntry<JsonElement> IAsyncEnumerator<CursorEntry<JsonElement>>.Current =>
        _current ?? throw new InvalidOperationException(
            "Cursor has no current entry. MoveNextAsync must be called first.");

    ValueTask<bool> IAsyncEnumerator<CursorEntry<JsonElement>>.MoveNextAsync() => MoveNextAsync(_enumCt);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await CursorRpc.ReleaseAsync(_tx, _cursorId).ConfigureAwait(false);
    }

    internal static CursorEntry<JsonElement> ParseEntry(JsonElement entry)
    {
        var key = IndexedDbWireFormat.FromKeyEnvelope(entry.GetProperty("key"));
        var primaryKey = IndexedDbWireFormat.FromKeyEnvelope(entry.GetProperty("primaryKey"));
        var value = entry.GetProperty("value").Clone();
        return new CursorEntry<JsonElement>(key, primaryKey, value);
    }
}
